// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BinaryFactor.InterpolatedTemplates;
using BinaryFactor.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator
{
    public class TypescriptGenerator
    {
        private readonly Action<string> log;

        public TypescriptGenerator(Action<string> log = null)
        {
            this.log = log ?? (msg => Console.WriteLine(msg));
        }

        public virtual IList<(TypeScriptModule module, string code)> GenerateCodeAndSave(string destinationFolder, bool forceCreateDestination = false)
        {
            this.log("Generating typescript files");

            try
            {
                var generatedModules = GenerateCode();

                foreach (var (module, code) in generatedModules)
                {
                    var filename = GetModuleFilename(module);
                    var destination = Path.Combine(destinationFolder, filename);

                    if (forceCreateDestination && !Directory.Exists(destination))
                        Directory.CreateDirectory(destination);

                    File.WriteAllText(destination, code);

                    this.log($"    {filename} generated at {Path.GetFullPath(destination)}");
                }

                return generatedModules;
            }
            catch(Exception e)
            {
                this.log($"ERROR GENERATING TYPESCRIPT FILES: {e}");
                throw;
            }
        }

        public virtual IList<(TypeScriptModule module, string code)> GenerateCode()
        {
            var moduleDefinitions = GetModuleDefinitions();

            var modules = CreateModulesFromModuleDefinitions(moduleDefinitions);

            return modules
                .Select(module =>
                {
                    var code = GenerateModule(module);
                    var formattedCode = new InterpolatedTemplateProcessor().Render(code);
                    return (module, formattedCode);
                })
                .ToList();
        }

        protected virtual string GetModuleFilename(TypeScriptModule module)
        {
            return $"{module.ModuleName}.ts";
        }

        protected virtual IList<TypeScriptModuleDefinition> GetModuleDefinitions()
        {
            return new[] { GetDefaultEnumsModuleDefinition(), GetDefaultDtoModuleDefinition(), GetDefaultApiModuleDefinition() };
        }

        protected virtual TypeScriptModuleDefinition GetDefaultEnumsModuleDefinition()
        {
            return new TypeScriptModuleDefinition(
                moduleName: "enums",
                typeCodeGenerator: GenerateEnum,
                typeFilter: type => type.IsEnum);
        }

        protected virtual TypeScriptModuleDefinition GetDefaultDtoModuleDefinition()
        {
            return new TypeScriptModuleDefinition(
                moduleName: "dto",
                typeCodeGenerator: GenerateDto,
                referencedModules: new[] { "enums" },
                assemblyFilter: assembly => assembly == GetEntryAssembly(),
                typeFilter: IsDto);
        }

        protected virtual TypeScriptModuleDefinition GetDefaultApiModuleDefinition()
        {
            return new TypeScriptModuleDefinition(
                moduleName: "api",
                typeCodeGenerator: GenerateController,
                referencedModules: new[] { "enums", "dto" },
                additionalContent: new FormattableString[] { $"import axios from 'axios';" },
                assemblyFilter: assembly => assembly == GetEntryAssembly(),
                typeFilter: IsNonAbstractController);
        }

        protected virtual IList<TypeScriptModule> CreateModulesFromModuleDefinitions(IList<TypeScriptModuleDefinition> moduleDefinitions)
        {
            var sortedModuleDefinitions = moduleDefinitions.TopologicallyOrderBy(comesBefore: (md1, md2) => md2.ReferencedModules.Contains(md1.ModuleName));
            var entryAssembly = GetEntryAssembly();
            var allAssembliesForGeneration = GetAllAssembliesForGeneration(entryAssembly);

            var modules = new Dictionary<string, TypeScriptModule>();

            foreach (var moduleDefinition in sortedModuleDefinitions)
            {
                var moduleAssemblies = allAssembliesForGeneration.Where(moduleDefinition.AssemblyFilter);

                var moduleTypes = moduleAssemblies
                    .SelectMany(assembly => assembly.GetExportedTypes())
                    .Where(moduleDefinition.TypeFilter)
                    .ToHashSet();

                modules[moduleDefinition.ModuleName] = new TypeScriptModule(
                    moduleDefinition.ModuleName,
                    moduleDefinition.TypeCodeGenerator,
                    moduleDefinition.AdditionalContent,
                    moduleDefinition.ReferencedModules.Select(rm => modules[rm]).ToHashSet(),
                    moduleTypes);
            }

            return modules.Values.ToList();
        }

        protected virtual Assembly GetEntryAssembly()
        {
            return Assembly.GetEntryAssembly();
        }

        protected virtual IList<Assembly> GetAllAssembliesForGeneration(Assembly entryAssembly)
        {
            return entryAssembly
                .GetReferencedAssemblies()
                .Where(assemblyName => IsOwnAssembly(assemblyName))
                .Select(assemblyName => Assembly.Load(assemblyName))
                .Concat(new[] { entryAssembly })
                .ToList();
        }

        protected virtual bool IsOwnAssembly(AssemblyName assemblyName)
        {
            var entryAssemblyName = GetEntryAssembly().FullName;

            var firstDotIndex = entryAssemblyName.IndexOf('.');
            var ownAssemblyPrefix = entryAssemblyName.Substring(0, firstDotIndex == -1 ? entryAssemblyName.Length : firstDotIndex);

            return assemblyName.FullName.StartsWith(ownAssemblyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        protected virtual bool IsOwnControllerMethod(MethodInfo method)
        {
            return IsOwnAssembly(method.DeclaringType.Assembly.GetName());
        }

        protected virtual bool IsDto(Type type)
        {
            return type.IsClass &&
                   (type.Namespace == type.Assembly.FullName + ".Models" ||
                    type.Namespace.StartsWith(type.Assembly.FullName + ".Models.") ||
                    type.IsNestedPublic && IsPossiblyAbstractController(type.DeclaringType));
        }

        protected virtual bool IsNonAbstractController(Type type)
        {
            return !type.IsAbstract && IsPossiblyAbstractController(type);
        }

        protected virtual bool IsPossiblyAbstractController(Type type)
        {
            return type.CustomAttrs().Has<ApiControllerAttribute>(inherit: true);
        }

        protected virtual FormattableString GenerateModule(TypeScriptModule module)
        {
            return $@"
                {GenerateHeader()}
                {module.ReferencedModules.SelectFS(importedModule => GenerateImport(module, importedModule))}

                {module.AdditionalContent}

                {GenerateModuleBody(module)}";
        }

        protected virtual FormattableString GenerateHeader()
        {
            return $"// This file is autogenerated, any manual changes will be lost after regeneration";
        }

        protected virtual FormattableString GenerateImport(TypeScriptModule currentModule, TypeScriptModule importedModule)
        {
            return $"import * as {importedModule.ModuleName} from './{importedModule.ModuleName}';";
        }

        protected virtual IEnumerable<FormattableString> GenerateModuleBody(TypeScriptModule module)
        {
            if (!module.Types.Any())
                return new FormattableString[] { $"export {{ }}" };

            var typesByNamespace = module.Types
                .GroupBy(type => CalculateTypescriptNamespace(module, type))
                .OrderBy(group => group.Key);

            return typesByNamespace.SelectFS(
                group => GenerateNamespace(
                    group.Key, group.OrderBy(type => type.Name).SelectFS(type => module.TypeCodeGenerator(module, type))));
        }

        protected virtual FormattableString GenerateNamespace(string @namespace, IEnumerable<FormattableString> code)
        {
            if (string.IsNullOrEmpty(@namespace))
                return $"{code}";

            return $@"
                export namespace {@namespace} {{
                    {code}
                }}
            ";
        }

        protected virtual string CalculateTypescriptNamespace(TypeScriptModule currentModule, Type type)
        {
            return "";
        }

        protected virtual FormattableString GenerateEnum(TypeScriptModule currentModule, Type type)
        {
            var enumName = type.Name;
            var enumItems = GetEnumItems(type);

            var enumItemDeclarations = enumItems.SelectFS(enumItem => $"{enumItem.name} = {enumItem.underlyingValue},");
            var enumDescriptionCases = enumItems.SelectFS(enumItem => $"case {enumName}.{enumItem.name}: return '{EscapeForJsString(enumItem.description)}';");
            var enumShortNameCases = enumItems.SelectFS(enumItem => $"case {enumName}.{enumItem.name}: return '{EscapeForJsString(enumItem.shortName)}';");
            var enumAllValues = enumItems.SelectFS(enumItem => $"{enumName}.{enumItem.name}").JoinBy($", ");

            return $@"
                export enum {enumName} {{
                    {enumItemDeclarations}
                }}

                export namespace {enumName} {{
                    export function getDescription(enumValue: {enumName}) {{
                        switch (enumValue) {{
                            {enumDescriptionCases}
                        }}
                    }}

                    export function getShortName(enumValue: {enumName}) {{
                        switch (enumValue) {{
                            {enumShortNameCases}
                        }}
                    }}

                    export function allEnums() {{
                        return [{enumAllValues}];
                    }}
                }}
            ";
        }

        protected virtual IList<(object underlyingValue, Enum enumValue, string name, string shortName, string description)> GetEnumItems(Type type)
        {
            return Enum.GetValues(type)
                .Cast<Enum>()
                .Select(enumValue =>
                    (underlyingValue: Convert.ChangeType(enumValue, Enum.GetUnderlyingType(type)),
                     enumValue,
                     name: Enum.GetName(type, enumValue),
                     shortName: GetEnumShortName(enumValue),
                     description: GetEnumDescription(enumValue)))
                .ToList();
        }

        protected virtual string GetEnumShortName(Enum value)
        {
            var displayAttribute = value.CustomAttrs().Get<DisplayAttribute>();

            return displayAttribute?.ShortName ?? ToSentenceCase(value.ToString());
        }

        protected virtual string GetEnumDescription(Enum value)
        {
            var displayAttribute = value.CustomAttrs().Get<DisplayAttribute>();

            return displayAttribute?.Description ??
                   displayAttribute?.ShortName ??
                   ToSentenceCase(value.ToString());
        }

        protected virtual FormattableString GenerateDto(TypeScriptModule currentModule, Type type)
        {
            var properties = GetDtoProperties(type);

            return $@"
                export interface {GetDtoTypeDeclaration(currentModule, type)} {{
                    {properties.SelectFS(property => GenerateDtoProperty(currentModule, property))}
                }}
            ";
        }

        protected IList<MemberInfo> GetDtoProperties(Type type)
        {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            return type
                .GetProperties(bindingFlags)
                .Cast<MemberInfo>()
                .Union(type.GetFields(bindingFlags))
                .ToList();
        }

        protected virtual string GetDtoTypeDeclaration(TypeScriptModule currentModule, Type type)
        {
            var baseTypeDeclaration = type.BaseType != typeof(object)
                ? $" extends {GetTypeScriptTypeName(currentModule, type.BaseType)}"
                : "";

            return NameWithGenericArguments(currentModule, type) + baseTypeDeclaration;
        }

        protected virtual FormattableString GenerateDtoProperty(TypeScriptModule currentModule, MemberInfo member)
        {
            var memberType =
                (member is FieldInfo fieldInfo) ? fieldInfo.FieldType :
                (member is PropertyInfo propertyInfo) ? propertyInfo.PropertyType :
                throw new ArgumentException();

            return $"{GetDtoPropertyJsName(member)}: {GetVariableTypeDeclaration(currentModule, memberType)};";
        }

        protected virtual string GetDtoPropertyJsName(MemberInfo member)
        {
            var jsonPropertyAttribute = member.CustomAttrs().Get("Newtonsoft.Json.JsonPropertyAttribute", inherit: true);
            if (jsonPropertyAttribute != null)
                return jsonPropertyAttribute.PropertyName;

            jsonPropertyAttribute = member.CustomAttrs().Get("System.Text.Json.Serialization.JsonPropertyNameAttribute", inherit: true);
            if (jsonPropertyAttribute != null)
                return jsonPropertyAttribute.Name;

            return ToCamelCase(member.Name);
        }

        protected virtual FormattableString GenerateController(TypeScriptModule currentModule, Type type)
        {
            var methods = GetControllerMethods(type);

            return $@"
                export namespace {type.Name} {{
                    {methods.SelectFS(method => GenerateControllerMethod(currentModule, method))}
                }}
            ";
        }

        protected virtual IList<MethodInfo> GetControllerMethods(Type type)
        {
            static bool IsPropertyGetterOrSetter(MethodInfo method)
            {
                return method.Name.StartsWith("get_") || method.Name.StartsWith("set_");
            }

            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;

            return type
                .GetMethods(bindingFlags)
                .Where(method => !IsPropertyGetterOrSetter(method) && IsOwnControllerMethod(method))
                .ToList();
        }

        protected virtual FormattableString GenerateControllerMethod(TypeScriptModule currentModule, MethodInfo method)
        {
            var returnTypeDeclaration = GetControllerReturnTypeDeclaration(currentModule, method);

            return GenerateControllerMethodDeclaration(currentModule, method, returnTypeDeclaration, GenerateControllerMethodBody);
        }

        protected virtual string GetControllerReturnTypeDeclaration(TypeScriptModule currentModule, MethodInfo method)
        {
            return GetVariableTypeDeclaration(currentModule, method.ReturnType.UnwrapPossibleTaskType());
        }

        protected virtual FormattableString GenerateControllerMethodDeclaration(TypeScriptModule currentModule, MethodInfo method, string returnTypeDeclaration, Func<MethodInfo, FormattableString> controllerMethodBodyGenerator)
        {
            var parameters = method
                .GetParameters()
                .Select(p => GetControllerParameterDeclaration(currentModule, p))
                .JoinBy(", ");

            return $@"
                export async function {ToCamelCase(method.Name)}({parameters}): Promise<{returnTypeDeclaration}> {{
                    {controllerMethodBodyGenerator(method)}
                }}";
        }

        protected virtual FormattableString GenerateControllerMethodBody(MethodInfo method)
        {
            var httpRequestSpec = GetHttpRequestSpec(method);
            return GenerateHttpRequest(httpRequestSpec);
        }

        protected virtual FormattableString GenerateHttpRequest(HttpRequestSpec httpRequestSpec)
        {
            return $@"
                const response = await axios.request({{
                    url: '{httpRequestSpec.Url}',
                    method: '{httpRequestSpec.HttpMethod}',
                    params: {{ {httpRequestSpec.QueryStringParameters.Select(p => p.Name).JoinBy(", ")} }},
                    data: {httpRequestSpec.BodyParameter?.Name ?? "null"}
                }});

                return response.data";
        }

        protected virtual string GetControllerParameterDeclaration(TypeScriptModule currentModule, ParameterInfo parameter)
        {
            return $"{parameter.Name}{(parameter.HasDefaultValue ? "?" : "")}: {GetVariableTypeDeclaration(currentModule, parameter.ParameterType)}";
        }

        protected virtual string GetVariableTypeDeclaration(TypeScriptModule currentModule, Type type)
        {
            var declaration = GetTypeScriptTypeName(currentModule, type);

            if (type.IsNullableValueType() || type.Is<string>())
                declaration += " | null";

            return declaration;
        }

        protected virtual string GetTypeScriptTypeName(TypeScriptModule currentModule, Type type)
        {
            type = type.UnwrapPossibleNullableType();

            if (type == typeof(void))
                return "void";

            if (type == typeof(object))
                return "any";

            if (type == typeof(string))
                return "string";

            if (type == typeof(bool))
                return "boolean";

            if (type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type.FullName == "NodaTime.Instant" ||
                type.FullName == "NodaTime.LocalDate" ||
                type.FullName == "NodaTime.LocalDateTime")
            {
                return "Date";
            }

            if (type == typeof(Guid))
                return "string";

            if (type.IsNumber())
                return "number";

            if (type == typeof(IFormFile))
                return "FormData";

            if (type == typeof(FileResult))
                return "any";

            if (type.IsAssignableToGenericType(typeof(IDictionary<,>)))
            {
                var genericArguments = type.GetGenericArguments(typeof(IDictionary<,>));
                var valueTypeName = GetTypeScriptTypeName(currentModule, genericArguments[1]);

                if (genericArguments[0] == typeof(string))
                    return $"{{[prop: string]: {valueTypeName}}}";

                if (genericArguments[0].IsNumber())
                    return $"{{[prop: number]: {valueTypeName}}}";

                return "any";
            }

            if (IsGenericCollection(type))
                return GetTypeScriptTypeName(currentModule, type.GetEnumerableItemType()) + "[]";

            if (IsNonGenericCollection(type))
                return "any[]";

            if (type.IsAssignableToGenericType(typeof(ActionResult<>)))
                return GetTypeScriptTypeName(currentModule, type.GetGenericArguments(typeof(ActionResult<>)).Single());

            if (type.Is<IActionResult>())
                return "any";

            if (type.IsGenericParameter)
                return type.Name;

            var typeModule = GetTypeModule(currentModule, type);
            return new[]
            {
                GetModuleReference(currentModule, typeModule),
                CalculateTypescriptNamespace(typeModule, type),
                NameWithGenericArguments(currentModule, type)
            }.JoinNonEmpty(".");
        }

        protected virtual string NameWithGenericArguments(TypeScriptModule currentModule, Type type)
        {
            if (!type.IsGenericType)
                return type.Name;

            var genericParameters = type
                .GetGenericArguments()
                .Select(genericArgumentType => GetTypeScriptTypeName(currentModule, genericArgumentType))
                .JoinBy(", ");

            return $"{StripGenericMarker(type.Name)}<{genericParameters}>";
        }

        protected virtual TypeScriptModule GetTypeModule(TypeScriptModule currentModule, Type type)
        {
            var typeModules = currentModule.ReferencedModules.Where(module => module.Types.Contains(type)).ToList();

            if (typeModules.Count == 0)
                throw new InvalidOperationException($"Could not find type '{type.FullName}' in any of the TypeScript modules referenced from the module '{currentModule.ModuleName}'");

            if (typeModules.Count > 1)
                throw new InvalidOperationException($"Type '{type.FullName}' was found in multiple modules referenced from the module '{currentModule.ModuleName}'");

            return typeModules[0];
        }

        protected virtual string GetModuleReference(TypeScriptModule currentModule, TypeScriptModule importedModule)
        {
            if (currentModule == importedModule)
                return "";

            return importedModule.ModuleName;
        }

        protected virtual string StripGenericMarker(string typeName)
        {
            if (!typeName.Contains("`"))
                return typeName;

            return typeName.Substring(0, typeName.IndexOf("`"));
        }

        protected virtual bool IsGenericCollection(Type type)
        {
            return type.IsGenericEnumerable();
        }

        protected virtual bool IsNonGenericCollection(Type type)
        {
            return type.IsEnumerable() && !type.IsGenericEnumerable();
        }

        protected virtual HttpRequestSpec GetHttpRequestSpec(MethodInfo method)
        {
            return new HttpRequestSpec(
                method,
                GetControllerActionUrl(method),
                GetControllerActionHttpMethod(method),
                GetControllerActionQueryStringParameters(method),
                GetControllerActionBodyParameter(method));
        }

        protected virtual string GetControllerActionHttpMethod(MethodInfo controllerAction)
        {
            return controllerAction.CustomAttrs().Get<IActionHttpMethodProvider>()?.HttpMethods.FirstOrDefault() ?? "GET";
        }

        // Routing and binding rules are described here:
        // https://docs.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-3.1
        protected virtual string GetControllerActionUrl(MethodInfo controllerAction)
        {
            var actionName = controllerAction.Name;
            var controllerName = GetControllerName(controllerAction.ReflectedType);
            var routeTemplate = GetControllerActionRouteTemplate(controllerAction);

            if (routeTemplate == null)
                throw new Exception($"Could not determine the route for api controller action {controllerName}.{actionName}");

            return routeTemplate
                .ReplaceIgnoreCase("[controller]", controllerName)
                .ReplaceIgnoreCase("{controller}", controllerName)
                .ReplaceIgnoreCase("[action]", actionName)
                .ReplaceIgnoreCase("{action}", actionName);
        }

        protected virtual IList<ParameterInfo> GetControllerActionQueryStringParameters(MethodInfo controllerAction)
        {
            return controllerAction
                .GetParameters()
                .Where(p => p != GetControllerActionBodyParameter(controllerAction))
                .ToList();
        }

        protected virtual ParameterInfo GetControllerActionBodyParameter(MethodInfo controllerAction)
        {
            var explicitBodyParameter =
                (from p in controllerAction.GetParameters()
                 let bindingSource = p.CustomAttrs().Get<IBindingSourceMetadata>()?.BindingSource
                 where bindingSource != null && bindingSource.CanAcceptDataFrom(BindingSource.Body)
                 select p).SingleOrDefault();

            if (explicitBodyParameter != null)
                return explicitBodyParameter;

            return controllerAction
                .GetParameters()
                .SingleOrDefault(p => (p.ParameterType.IsClass && p.ParameterType != typeof(string)) ||
                                      p.ParameterType == typeof(IFormFile));
        }

        protected virtual string GetControllerName(Type controllerType)
        {
            var controllerName = controllerType.Name;

            if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
                return controllerName.Remove(controllerName.Length - "Controller".Length);

            return controllerName;
        }

        protected virtual string GetControllerActionRouteTemplate(MethodInfo controllerAction)
        {
            var methodRouteTemplate = controllerAction
                .CustomAttrs()
                .GetAll<IRouteTemplateProvider>(inherit: true)
                .OrderBy(attr => attr.Order)
                .FirstOrDefault()?.Template;

            var classRouteTemplate = controllerAction.DeclaringType
                .CustomAttrs()
                .GetAll<IRouteTemplateProvider>(inherit: true)
                .OrderBy(attr => attr.Order)
                .FirstOrDefault()?.Template;

            return CombineRouteTemplates(classRouteTemplate, methodRouteTemplate);
        }

        protected virtual string CombineRouteTemplates(string classRouteTemplate, string methodRouteTemplate)
        {
            var result = "/" + classRouteTemplate + "/" + methodRouteTemplate;
            result = result.Replace("//", "/");

            if (result.EndsWith("/"))
                result = result.Substring(0, result.Length - 1);

            return result;
        }

        protected virtual string EscapeForJsString(string str)
        {
            return str
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        protected virtual string ToCamelCase(string str)
        {
            if (!string.IsNullOrEmpty(str) && char.IsUpper(str[0]))
                return char.ToLowerInvariant(str[0]) + str.Substring(1);

            return str;
        }

        protected virtual string ToSentenceCase(string text, bool capitaliseWords = false)
        {
            return Regex.Replace(text, "[a-z][A-Z]",
                m => string.Concat(m.Value[0], ' ', capitaliseWords ? m.Value[1] : char.ToLower(m.Value[1])));
        }

        protected class TypeScriptModuleDefinition
        {
            public TypeScriptModuleDefinition(
                string moduleName,
                Func<TypeScriptModule, Type, FormattableString> typeCodeGenerator,
                IList<string> referencedModules = null,
                IList<FormattableString> additionalContent = null,
                Func<Assembly, bool> assemblyFilter = null,
                Func<Type, bool> typeFilter = null)
            {
                ModuleName = moduleName;
                TypeCodeGenerator = typeCodeGenerator;
                ReferencedModules = referencedModules ?? new List<string>();
                AdditionalContent = additionalContent ?? new List<FormattableString>();
                AssemblyFilter = assemblyFilter ?? (_ => true);
                TypeFilter = typeFilter ?? (_ => true);
            }

            public string ModuleName { get; }
            public Func<TypeScriptModule, Type, FormattableString> TypeCodeGenerator { get; }
            public IList<string> ReferencedModules { get; }
            public IList<FormattableString> AdditionalContent { get; }
            public Func<Assembly, bool> AssemblyFilter { get; }
            public Func<Type, bool> TypeFilter { get; }
        }

        public class TypeScriptModule
        {
            public TypeScriptModule(
                string moduleName,
                Func<TypeScriptModule, Type, FormattableString> typeCodeGenerator,
                IList<FormattableString> additionalContent,
                ISet<TypeScriptModule> referencedModules,
                ISet<Type> types)
            {
                ModuleName = moduleName;
                TypeCodeGenerator = typeCodeGenerator;
                AdditionalContent = additionalContent;
                ReferencedModules = referencedModules;
                Types = types;
            }

            public string ModuleName { get; }
            public Func<TypeScriptModule, Type, FormattableString> TypeCodeGenerator { get; }
            public IList<FormattableString> AdditionalContent { get; }
            public ISet<TypeScriptModule> ReferencedModules { get; }
            public ISet<Type> Types { get; }
        }

        protected class HttpRequestSpec
        {
            public HttpRequestSpec(MethodInfo method, string url, string httpMethod, IList<ParameterInfo> queryStringParameters, ParameterInfo bodyParameter)
            {
                Method = method;
                Url = url;
                HttpMethod = httpMethod;
                QueryStringParameters = queryStringParameters;
                BodyParameter = bodyParameter;
            }

            public MethodInfo Method { get; }
            public string Url { get; }
            public string HttpMethod { get; }
            public IList<ParameterInfo> QueryStringParameters { get; }
            public ParameterInfo BodyParameter { get; }
        }
    }
}
