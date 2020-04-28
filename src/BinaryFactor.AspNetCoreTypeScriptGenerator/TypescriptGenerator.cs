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
    public class TypeScriptGenerator
    {
        private readonly TypeScriptGeneratorOptions options;

        public TypeScriptGenerator()
            : this(new TypeScriptGeneratorOptions())
        {
        }

        public TypeScriptGenerator(TypeScriptGeneratorOptions options)
        {
            this.options = options;
        }

        public virtual IList<(string module, string code)> GenerateCodeAndSave(string destinationFolder, bool forceCreateDestination = false)
        {
            this.options.Logger("Generating TypeScript files");

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

                    this.options.Logger($"    {filename} generated at {Path.GetFullPath(destination)}");
                }

                return generatedModules;
            }
            catch (Exception e)
            {
                this.options.Logger($"ERROR GENERATING TYPESCRIPT FILES: {e}");
                throw;
            }
        }

        public virtual IList<(string module, string code)> GenerateCode()
        {
            var typesWithDependencies = GetAllReferencedTypes(GetEntryTypes());

            var modulesWithTypesAndImports = typesWithDependencies
                .GroupBy(
                    typeWithDependencies => GetModule(typeWithDependencies.Key),
                    (module, moduleTypesWithDependencies) =>
                        (moduleName: module,
                         types: moduleTypesWithDependencies.Select(moduleType => moduleType.Key),
                         imports: moduleTypesWithDependencies
                                    .SelectMany(moduleType => moduleType.Value.Select(type => GetModule(type)))
                                    .Where(import => !string.IsNullOrEmpty(import) && import != module)
                                    .Distinct()
                                    .OrderBy(import => import)
                                    .ToList()))
                .Where(module => !string.IsNullOrEmpty(module.moduleName))
                .ToList();

            var result = modulesWithTypesAndImports
                .Select(module =>
                {
                    var generatedCode = GenerateModule(
                        module.moduleName,
                        module.types,
                        module.imports,
                        GetDefaultModuleImports(module.moduleName).Concat(this.options.ModuleImports(module.moduleName)),
                        this.options.AdditionalModuleContent(module.moduleName));

                    var formattedCode = new InterpolatedTemplateProcessor().Render(generatedCode);

                    return (module.moduleName, formattedCode);
                }).ToList();

            return result;
        }

        protected virtual string GetModuleFilename(string module)
        {
            return $"{module}.ts";
        }

        public virtual IList<Type> GetEntryTypes()
        {
            return this.options.EntryAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .Where(IsNonAbstractController)
                .Concat(this.options.AdditionalEntryTypes)
                .ToList();
        }

        public virtual IDictionary<Type, ISet<Type>> GetAllReferencedTypes(IList<Type> entryTypes)
        {
            var pendingTypes = new HashSet<Type>(entryTypes);
            var result = pendingTypes.ToDictionary(type => type, _ => (ISet<Type>) new HashSet<Type>());

            void Visit(Type type)
            {
                if (!result.ContainsKey(type))
                {
                    pendingTypes.Add(type);
                    result.Add(type, new HashSet<Type>());
                }
            }

            void AddToResult(Type dependent, IEnumerable<Type> dependencies)
            {
                Visit(dependent);

                foreach (var type in dependencies.SelectMany(UnwrapPossibleCollectionType))
                {
                    if (!IsOwnType(type))
                        continue;

                    Visit(type);

                    if (!result[dependent].Contains(type))
                        result[dependent].Add(type);
                }
            }

            while (pendingTypes.Count > 0)
            {
                var type = pendingTypes.First();
                AddToResult(type, GetDependencies(type));
                pendingTypes.Remove(type);
            }

            return result;
        }

        protected virtual IList<Type> GetDependencies(Type type)
        {
            var result = new List<Type>();

            if (IsNonAbstractController(type))
            {
                var methods = GetControllerMethods(type);
                result.AddRange(methods.Select(method => method.ReturnType));
                result.AddRange(methods.SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType));
            }
            else if (IsDto(type))
            {
                if (type.BaseType != null)
                    result.Add(type.BaseType);

                result.AddRange(type.GetInterfaces());

                var properties = GetDtoProperties(type);
                result.AddRange(properties.Select(GetFieldOrPropertyType));
            }

            return result;
        }

        protected virtual string GetModule(Type type)
        {
            if (type.IsEnum)
                return DefaultModules.Enums;

            if (IsNonAbstractController(type))
                return DefaultModules.Api;

            if (IsDto(type))
                return DefaultModules.Dto;

            return null;
        }

        protected IList<FormattableString> GetDefaultModuleImports(string module)
        {
            if (module == DefaultModules.Api)
                return new FormattableString[] { $"import axios from 'axios';" };

            return new FormattableString[] { };
        }

        protected virtual Func<string, Type, FormattableString> GetCodeGenerator(string module, Type type)
        {
            if (type.IsEnum)
                return GenerateEnum;

            if (IsNonAbstractController(type))
                return GenerateController;

            if (IsDto(type))
                return GenerateDto;

            return null;
        }

        protected virtual bool IsOwnAssembly(Assembly assembly)
        {
            var entryAssemblyNames = this.options.EntryAssemblies.Select(assembly => assembly.GetName().Name).ToList();

            return entryAssemblyNames.Any(entryAssemblyName =>
            {
                var firstDotIndex = entryAssemblyName.IndexOf('.');
                var ownAssemblyPrefix = entryAssemblyName.Substring(0, firstDotIndex == -1 ? entryAssemblyName.Length : firstDotIndex);

                return assembly.GetName().FullName.StartsWith(ownAssemblyPrefix, StringComparison.OrdinalIgnoreCase);
            });
        }

        protected virtual bool IsOwnType(Type type)
        {
            return this.options.IsOwnTypeChecker?.Invoke(type) ?? IsOwnAssembly(type.Assembly);
        }

        protected virtual bool IsOwnControllerMethod(MethodInfo method)
        {
            return IsOwnType(method.DeclaringType);
        }

        protected virtual bool IsNonAbstractController(Type type)
        {
            return !type.IsAbstract && IsPossiblyAbstractController(type);
        }

        protected virtual bool IsPossiblyAbstractController(Type type)
        {
            return type.CustomAttrs().Has<ApiControllerAttribute>(inherit: true);
        }

        protected virtual bool IsDto(Type type)
        {
            return !type.IsEnum && !IsPossiblyAbstractController(type);
        }

        protected virtual FormattableString GenerateModule(string moduleName, IEnumerable<Type> types, IEnumerable<string> imports, IEnumerable<FormattableString> additionalImports, IEnumerable<FormattableString> additionalContent)
        {
            return $@"
                {this.options.Header}
                {imports.SelectFS(importedModule => GenerateImport(moduleName, importedModule))}
                {additionalImports}

                {additionalContent}

                {GenerateModuleBody(moduleName, types)}";
        }

        protected virtual FormattableString GenerateImport(string currentModule, string importedModule)
        {
            return $"import * as {importedModule} from './{importedModule}';";
        }

        protected virtual IEnumerable<FormattableString> GenerateModuleBody(string currentModule, IEnumerable<Type> types)
        {
            if (!types.Any())
                return new FormattableString[] { $"export {{ }}" };

            var typesByNamespace = types
                .GroupBy(type => CalculateTypeScriptNamespace(currentModule, type))
                .OrderBy(group => group.Key);

            return typesByNamespace.SelectFS(
                group => GenerateNamespace(
                    group.Key,
                    group
                        .OrderBy(type => type.Name)
                        .Select(type => (type, codeGenerator: GetCodeGenerator(currentModule, type)))
                        .Where(typeWithCodeGen => typeWithCodeGen.codeGenerator != null)
                        .SelectFS(typeWithCodeGen => typeWithCodeGen.codeGenerator(currentModule, typeWithCodeGen.type))));
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

        protected virtual string CalculateTypeScriptNamespace(string currentModule, Type type)
        {
            return "";
        }

        protected virtual FormattableString GenerateEnum(string currentModule, Type type)
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

        protected virtual FormattableString GenerateDto(string currentModule, Type type)
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
                .Where(property => property.CanRead)
                .Cast<MemberInfo>()
                .Union(type.GetFields(bindingFlags))
                .ToList();
        }

        protected virtual string GetDtoTypeDeclaration(string currentModule, Type type)
        {
            var baseTypeDeclaration = type.BaseType != typeof(object)
                ? $" extends {GetTypeScriptTypeName(currentModule, type.BaseType)}"
                : "";

            return NameWithGenericArguments(currentModule, type) + baseTypeDeclaration;
        }

        protected virtual FormattableString GenerateDtoProperty(string currentModule, MemberInfo member)
        {
            return $"{GetDtoPropertyJsName(member)}: {GetDtoPropertyTypeDeclaration(currentModule, member)};";
        }

        protected virtual FormattableString GetDtoPropertyTypeDeclaration(string currentModule, MemberInfo member)
        {
            var memberType = GetFieldOrPropertyType(member);

            var customAttributeProviders =
                member is FieldInfo fieldInfo ? new ICustomAttributeProvider[] { fieldInfo } :
                member is PropertyInfo propertyInfo ? new ICustomAttributeProvider[] { propertyInfo, propertyInfo.GetMethod, propertyInfo.GetMethod.ReturnTypeCustomAttributes } :
                throw new ArgumentException();

            return GetVariableTypeDeclaration(currentModule, memberType, customAttributeProviders);
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

        protected virtual FormattableString GenerateController(string currentModule, Type type)
        {
            var methods = GetControllerMethods(type);

            return $@"
                export namespace {type.Name} {{
                    {methods.SelectFS(method => GenerateControllerMethod(currentModule, method)).AppendToEach($"{Environment.NewLine}")}
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

        protected virtual FormattableString GenerateControllerMethod(string currentModule, MethodInfo method)
        {
            var returnTypeDeclaration = GetControllerReturnTypeDeclaration(currentModule, method);

            return GenerateControllerMethodDeclaration(currentModule, method, returnTypeDeclaration, GenerateControllerMethodBody);
        }

        protected virtual FormattableString GetControllerReturnTypeDeclaration(string currentModule, MethodInfo method)
        {
            return GetVariableTypeDeclaration(currentModule, method.ReturnType.UnwrapPossibleTaskType(), method, method.ReturnTypeCustomAttributes);
        }

        protected virtual FormattableString GenerateControllerMethodDeclaration(string currentModule, MethodInfo method, FormattableString returnTypeDeclaration, Func<MethodInfo, FormattableString> controllerMethodBodyGenerator)
        {
            var parameters = method
                .GetParameters()
                .Select(p => GetControllerParameterDeclaration(currentModule, p))
                .JoinBy($", ");

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
                    url: {GetRequestUrlExpression(httpRequestSpec.Url)},
                    method: '{httpRequestSpec.HttpMethod}',
                    params: {{ {httpRequestSpec.QueryStringParameters.Select(p => p.Name).JoinBy(", ")} }},
                    data: {httpRequestSpec.BodyParameter?.Name ?? "null"}
                }});

                return response.data";
        }

        protected virtual FormattableString GetRequestUrlExpression(string url)
        {
            return this.options.RequestUrlExpression?.Invoke(url) ?? $"'{url}'";
        }

        protected virtual FormattableString GetControllerParameterDeclaration(string currentModule, ParameterInfo parameter)
        {
            return $"{parameter.Name}{(parameter.HasDefaultValue ? "?" : "")}: {GetControllerParameterTypeDeclaration(currentModule, parameter)}";
        }

        protected virtual FormattableString GetControllerParameterTypeDeclaration(string currentModule, ParameterInfo parameter)
        {
            return GetVariableTypeDeclaration(currentModule, parameter.ParameterType, parameter);
        }

        protected virtual FormattableString GetVariableTypeDeclaration(string currentModule, Type type, params ICustomAttributeProvider[] attributeProviders)
        {
            FormattableString declaration = $"{GetTypeScriptTypeName(currentModule, type)}";

            return ShouldMakeDeclarationTypeNullable(type, attributeProviders)
                ? MarkTypeDeclarationAsNullable(declaration)
                : declaration;
        }

        protected virtual FormattableString MarkTypeDeclarationAsNullable(FormattableString typeDeclaration)
        {
            var nullMarker = this.options.NullableTypeMapping switch
            {
                NullableTypeMapping.Null => "null",
                NullableTypeMapping.Undefined => "undefined",
                NullableTypeMapping.NullOrUndefined => "undefined | null",

                _ => throw new ArgumentException()
            };

            return $"{typeDeclaration} | {nullMarker}";
        }

        protected virtual bool ShouldMakeDeclarationTypeNullable(Type type, params ICustomAttributeProvider[] attributeProviders)
        {
            foreach (var attributeProvider in attributeProviders)
            {
                var customAttrs = attributeProvider.CustomAttrs();

                if (customAttrs.Has("System.Diagnostics.CodeAnalysis.DisallowNullAttribute") ||
                    customAttrs.Has("System.Diagnostics.CodeAnalysis.NotNullAttribute") ||
                    customAttrs.Has("JetBrains.Annotations.NotNullAttribute"))
                {
                    return false;
                }

                if (customAttrs.Has("System.Diagnostics.CodeAnalysis.MaybeNullAttribute") ||
                    customAttrs.Has("System.Diagnostics.CodeAnalysis.AllowNullAttribute") ||
                    customAttrs.Has("System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute") ||
                    customAttrs.Has("JetBrains.Annotations.CanBeNullAttribute"))
                {
                    return true;
                }
            }

            if (type.IsNullableValueType())
            {
                return true;
            }

            if (type.Is<string>() && this.options.StringsAreNullableByDefault)
            {
                return true;
            }

            return false;
        }

        protected virtual string GetTypeScriptTypeName(string currentModule, Type type)
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
                var genericArguments = type.ExtractGenericArguments(typeof(IDictionary<,>));
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
                return GetTypeScriptTypeName(currentModule, type.ExtractGenericArguments(typeof(ActionResult<>)).Single());

            if (type.Is<IActionResult>())
                return "any";

            if (type.IsGenericParameter)
                return type.Name;

            var typeModule = GetModule(type);
            return new[]
            {
                GetModuleReference(currentModule, typeModule),
                CalculateTypeScriptNamespace(typeModule, type),
                NameWithGenericArguments(currentModule, type)
            }.JoinNonEmpty(".");
        }

        protected virtual string NameWithGenericArguments(string currentModule, Type type)
        {
            if (!type.IsGenericType)
                return type.Name;

            var genericParameters = type
                .GetGenericArguments()
                .Select(genericArgumentType => GetTypeScriptTypeName(currentModule, genericArgumentType))
                .JoinBy(", ");

            return $"{StripGenericMarker(type.Name)}<{genericParameters}>";
        }

        protected virtual string GetModuleReference(string currentModule, string importedModule)
        {
            if (currentModule == importedModule)
                return "";

            return importedModule;
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
            return Regex.Replace(
                text,
                "[a-z][A-Z]",
                m => string.Concat(m.Value[0], ' ', capitaliseWords ? m.Value[1] : char.ToLower(m.Value[1])));
        }

        protected virtual Type GetFieldOrPropertyType(MemberInfo member)
        {
            return member is FieldInfo fieldInfo ? fieldInfo.FieldType :
                   member is PropertyInfo propertyInfo ? propertyInfo.PropertyType :
                   throw new ArgumentException();
        }

        protected virtual IEnumerable<Type> UnwrapPossibleCollectionType(Type type)
        {
            if (type.IsAssignableToGenericType(typeof(IDictionary<,>)))
                return type.ExtractGenericArguments(typeof(IDictionary<,>)).SelectMany(UnwrapPossibleCollectionType);

            if (type.IsGenericEnumerable())
                return UnwrapPossibleCollectionType(type.GetEnumerableItemType());

            return new[] { type };
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

        class DefaultModules
        {
            public const string Enums = "enums";
            public const string Dto = "dto";
            public const string Api = "api";
        }
    }
}
