// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BinaryFactor.InterpolatedTemplates;
using BinaryFactor.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
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
                        GetDefaultModuleImports(module.moduleName).Concat(this.options.AdditionalModuleImports(module.moduleName)),
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
            var pendingTypes = new HashSet<Type>(entryTypes.Where(type => this.options.TypeFilter(type)));
            var result = pendingTypes.ToDictionary(type => type, _ => (ISet<Type>) new HashSet<Type>());

            void Visit(Type type)
            {
                if (!result.ContainsKey(type))
                {
                    pendingTypes.Add(type);
                    result.Add(type, new HashSet<Type>());
                }
            }

            void AddToResult(Type dependent, IEnumerable<TypeWithNullabilityContext> dependencies)
            {
                foreach (var type in dependencies.Select(t => GetTypeRef(t)).SelectMany(typeRef => typeRef.GetDependencies()))
                {
                    if (!this.options.TypeFilter(type))
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

        protected virtual IList<TypeWithNullabilityContext> GetDependencies(Type type)
        {
            var result = new List<TypeWithNullabilityContext>();

            if (IsNonAbstractController(type))
            {
                var methods = GetControllerMethods(type);
                result.AddRange(methods.Select(method => UnwrapControllerActionReturnType(TypeWithNullabilityContext.MethodReturnType(method))));
                result.AddRange(methods.SelectMany(method => method.GetParameters()).Select(TypeWithNullabilityContext.ParameterType));
            }
            else if (IsDto(type))
            {
                if (type.BaseType != null)
                    TypeWithNullabilityContext.BaseType(type);

                result.AddRange(TypeWithNullabilityContext.ImplementedInterfaces(type));

                var properties = GetDtoProperties(type);
                result.AddRange(properties.Select(TypeWithNullabilityContext.FieldOrPropertyType));
            }

            return result;
        }

        protected virtual TypeWithNullabilityContext UnwrapControllerActionReturnType(TypeWithNullabilityContext type)
        {
            if (type.IsAssignableToGenericType(typeof(Task<>)))
                type = type.ExtractGenericArguments(typeof(Task<>)).Single();

            if (type.GetClrType().Is<Task>())
                return TypeWithNullabilityContext.Contextless(typeof(void));

            if (type.IsAssignableToGenericType(typeof(ActionResult<>)))
                return type.ExtractGenericArguments(typeof(ActionResult<>)).Single();

            if (type.GetClrType().Is<IActionResult>() || type.GetClrType().Is<IConvertToActionResult>())
                return TypeWithNullabilityContext.Contextless(typeof(object));

            return type;
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

        protected virtual bool IsControllerAction(MethodInfo method)
        {
            return !new[]
            {
                "Microsoft.AspNetCore.Mvc.Controller",
                "Microsoft.AspNetCore.Mvc.ControllerBase",
                "System.Object"
            }.Contains(method.DeclaringType.FullName) && this.options.TypeFilter(method.DeclaringType) && method.IsPublic;
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

        protected virtual FormattableString GetDtoTypeDeclaration(string currentModule, Type type)
        {
            var typeRef = GetTypeRef(TypeWithNullabilityContext.Contextless(type));
            var declaration = GetTypeScriptTypeName(currentModule, typeRef);

            if (type.BaseType == null || type.BaseType == typeof(object))
                return $"{declaration}";

            var baseTypeRef = GetTypeRef(TypeWithNullabilityContext.BaseType(type));
            return $"{declaration} extends {GetTypeScriptTypeName(currentModule, baseTypeRef)}";
        }

        protected virtual FormattableString GenerateDtoProperty(string currentModule, MemberInfo member)
        {
            var propertyType = TypeWithNullabilityContext.FieldOrPropertyType(member);
            var propertyTypeRef = GetTypeRef(propertyType, this.options.PropertyNullableTypeMapping);
            var propertyName = GetDtoPropertyJsName(member);

            if (this.options.MakeUndefinedPropertiesOptional && propertyTypeRef.Is(TypeRef.Undefined))
            {
                propertyTypeRef = propertyTypeRef.Subtract(TypeRef.Undefined);
                propertyName += "?";
            }

            return $"{propertyName}: {GetVariableTypeDeclaration(currentModule, propertyTypeRef)};";
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
                .Where(method => !IsPropertyGetterOrSetter(method) && IsControllerAction(method))
                .ToList();
        }

        protected virtual FormattableString GenerateControllerMethod(string currentModule, MethodInfo method)
        {
            var returnTypeDeclaration = GetControllerReturnTypeDeclaration(currentModule, method);

            return GenerateControllerMethodDeclaration(currentModule, method, returnTypeDeclaration, GenerateControllerMethodBody);
        }

        protected virtual FormattableString GetControllerReturnTypeDeclaration(string currentModule, MethodInfo method)
        {
            var methodReturnType = TypeWithNullabilityContext.MethodReturnType(method);
            methodReturnType = UnwrapControllerActionReturnType(methodReturnType);

            var methodReturnTypeRef = GetTypeRef(methodReturnType);

            return GetVariableTypeDeclaration(currentModule, methodReturnTypeRef);
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
            return this.options.RequestUrlExpressionGenerator?.Invoke(url) ?? $"'{url}'";
        }

        protected virtual FormattableString GetControllerParameterDeclaration(string currentModule, ParameterInfo parameter)
        {
            var parameterType = TypeWithNullabilityContext.ParameterType(parameter);
            var parameterTypeRef = GetTypeRef(parameterType, this.options.ParameterNullableTypeMapping);
            var parameterName = parameter.Name;

            if (this.options.MakeUndefinedParametersOptional && parameterTypeRef.Is(TypeRef.Undefined))
            {
                parameterTypeRef = parameterTypeRef.Subtract(TypeRef.Undefined);
                parameterName += "?";
            }
            else if (parameter.HasDefaultValue)
            {
                parameterName += "?";
            }

            return $"{parameterName}: {GetVariableTypeDeclaration(currentModule, parameterTypeRef)}";
        }

        protected virtual FormattableString GetVariableTypeDeclaration(string currentModule, TypeRef typeRef)
        {
            return $"{GetTypeScriptTypeName(currentModule, typeRef)}";
        }

        protected virtual TypeRef GetNonNullableTypeRef(TypeWithNullabilityContext type)
        {
            if (!this.options.TypeFilter(type.GetClrType()))
                return TypeRef.Any;

            if (type.GetClrType() == typeof(void))
                return TypeRef.Void;

            if (type.IsGenericParameter)
                return TypeRef.GenericArg(type.GetClrType());

            if (type.GetClrType() == typeof(object))
                return TypeRef.Any;

            if (type.GetClrType() == typeof(string))
                return TypeRef.String;

            if (type.GetClrType() == typeof(bool))
                return TypeRef.Boolean;

            if (type.GetClrType() == typeof(DateTime) ||
                type.GetClrType() == typeof(DateTimeOffset) ||
                type.FullName == "NodaTime.Instant" ||
                type.FullName == "NodaTime.LocalDate" ||
                type.FullName == "NodaTime.LocalDateTime")
            {
                return TypeRef.Date;
            }

            if (type.GetClrType() == typeof(Guid))
                return TypeRef.String;

            if (type.GetClrType().IsNumber())
                return TypeRef.Number;

            if (type.GetClrType() == typeof(IFormFile))
                return TypeRef.FormData;

            if (type.GetClrType() == typeof(FileResult))
                return TypeRef.Any;

            if (type.IsAssignableToGenericType(typeof(IDictionary<,>)))
            {
                var genericArguments = type.ExtractGenericArguments(typeof(IDictionary<,>));

                var keyType = genericArguments[0];
                var valueType = genericArguments[1];

                if (keyType.GetClrType() == typeof(string) || keyType.GetClrType().IsNumber())
                    return TypeRef.Dict(GetTypeRef(keyType), GetTypeRef(valueType));

                if (keyType.GetClrType().IsEnum)
                    return TypeRef.MappedType(GetTypeRef(keyType), GetTypeRef(valueType));

                return TypeRef.Any;
            }

            if (IsGenericCollection(type.GetClrType()))
            {
                var collectionType = type.ExtractGenericArguments(typeof(IEnumerable<>)).Single();
                return TypeRef.ArrayOf(GetTypeRef(collectionType));
            }

            if (IsNonGenericCollection(type.GetClrType()))
                return TypeRef.ArrayOf(TypeRef.Any);

            if (type.IsGenericType)
                return TypeRef.GenericType(TypeRef.UserType(type.GetClrType()), type.GetGenericArguments().Select(arg => GetTypeRef(arg)).ToList());

            return TypeRef.UserType(type.GetClrType());
        }

        protected virtual TypeRef GetTypeRef(TypeWithNullabilityContext type, NullableTypeMapping? preferredTypeMapping = null)
        {
            var nonNullableTypeRef = GetNonNullableTypeRef(type);

            var shouldBeNullable = ShouldBeNullable(type);

            if (shouldBeNullable == false)
                return nonNullableTypeRef;

            if (shouldBeNullable == true || type.NullabilityType == NullabilityStatus.Null)
                return MakeNullable(nonNullableTypeRef, preferredTypeMapping);

            return nonNullableTypeRef;
        }

        protected virtual bool? ShouldBeNullable(TypeWithNullabilityContext type)
        {
            if (type.GetClrType() == typeof(string) && type.NullabilityType == NullabilityStatus.Oblivious && this.options.StringsAreNullableByDefault)
                return true;

            return null;
        }

        protected virtual TypeRef MakeNullable(TypeRef typeRef, NullableTypeMapping? preferredTypeMapping = null)
        {
            var nullableTypeMapping = preferredTypeMapping ?? this.options.DefaultNullableTypeMapping;

            return nullableTypeMapping switch
            {
                NullableTypeMapping.Null => TypeRef.Union(typeRef, TypeRef.Null),
                NullableTypeMapping.Undefined => TypeRef.Union(typeRef, TypeRef.Undefined),
                NullableTypeMapping.NullOrUndefined => TypeRef.Union(typeRef, TypeRef.Undefined, TypeRef.Null),

                _ => throw new ArgumentException(),
            };
        }

        protected virtual string GetTypeScriptTypeName(string currentModule, TypeRef typeRef)
        {
            return typeRef.Render(type =>
            {
                var typeModule = GetModule(type);
                return new[]
                {
                    GetModuleReference(currentModule, typeModule),
                    CalculateTypeScriptNamespace(typeModule, type),
                    CalculateTypeScriptTypeName(typeModule, type)
                }.JoinNonEmpty(".");
            });
        }

        protected virtual string CalculateTypeScriptTypeName(string typeModule, Type type)
        {
            return StripGenericMarker(type.Name);
        }

        protected virtual string CalculateTypeScriptNamespace(string typeModule, Type type)
        {
            return this.options.NamespaceCalculator(typeModule, type);
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

        protected enum NullabilityStatus
        {
            NotNull,
            Null,
            Oblivious
        }

        protected class TypeWithNullabilityContext
        {
            private readonly Type clrType;

            private TypeWithNullabilityContext(Type clrType, params ICustomAttributeProvider[] customAttributeProviders)
            {
                CustomAttributeProviders = customAttributeProviders;

                if (clrType.IsNullableValueType())
                {
                    this.clrType = clrType.UnwrapPossibleNullableType();
                    NullabilityType = NullabilityStatus.Null;
                }
                else if (clrType.IsValueType)
                {
                    this.clrType = clrType;
                    NullabilityType = NullabilityStatus.NotNull;
                }
                else
                {
                    this.clrType = clrType;
                    NullabilityType = GetReferenceTypeNullability(customAttributeProviders);
                }
            }

            public IList<TypeWithNullabilityContext> ExtractGenericArguments(Type genericTypeDefinition)
            {
                return this.clrType
                    .ExtractGenericArguments(genericTypeDefinition)
                    .Select(arg => Contextless(arg))
                    .ToList();
            }

            public TypeWithNullabilityContext[] GetGenericArguments()
            {
                return this.clrType.GetGenericArguments().Select(arg => Contextless(arg)).ToArray();
            }

            public string Name => this.clrType.Name;
            public string FullName => this.clrType.FullName;
            public bool IsGenericType => this.clrType.IsGenericType;
            public bool IsGenericParameter => this.clrType.IsGenericParameter;
            public bool IsAssignableToGenericType(Type genericType) => this.clrType.IsAssignableToGenericType(genericType);

            public Type GetClrType() => this.clrType;

            public NullabilityStatus NullabilityType { get; }

            public ICustomAttributeProvider[] CustomAttributeProviders { get; }

            public static TypeWithNullabilityContext FieldOrPropertyType(MemberInfo member)
            {
                if (member is FieldInfo fieldInfo)
                    return new TypeWithNullabilityContext(fieldInfo.FieldType, fieldInfo);

                if (member is PropertyInfo propertyInfo)
                    return new TypeWithNullabilityContext(propertyInfo.PropertyType, propertyInfo, propertyInfo.GetMethod, propertyInfo.GetMethod.ReturnTypeCustomAttributes);

                throw new ArgumentException();
            }

            public static TypeWithNullabilityContext MethodReturnType(MethodInfo method)
            {
                return new TypeWithNullabilityContext(method.ReturnType, method, method.ReturnTypeCustomAttributes);
            }

            public static TypeWithNullabilityContext ParameterType(ParameterInfo parameter)
            {
                return new TypeWithNullabilityContext(parameter.ParameterType, parameter);
            }

            public static TypeWithNullabilityContext BaseType(Type type)
            {
                return Contextless(type.BaseType);
            }

            public static TypeWithNullabilityContext Contextless(Type type)
            {
                return new TypeWithNullabilityContext(type);
            }

            public static IList<TypeWithNullabilityContext> ImplementedInterfaces(Type type)
            {
                return type.GetInterfaces().Select(Contextless).ToList();
            }

            private static NullabilityStatus GetReferenceTypeNullability(ICustomAttributeProvider[] attributeProviders)
            {
                foreach (var attributeProvider in attributeProviders)
                {
                    var customAttrs = attributeProvider.CustomAttrs();

                    if (customAttrs.Has("System.Diagnostics.CodeAnalysis.DisallowNullAttribute") ||
                        customAttrs.Has("System.Diagnostics.CodeAnalysis.NotNullAttribute") ||
                        customAttrs.Has("JetBrains.Annotations.NotNullAttribute"))
                    {
                        return NullabilityStatus.NotNull;
                    }

                    if (customAttrs.Has("System.Diagnostics.CodeAnalysis.MaybeNullAttribute") ||
                        customAttrs.Has("System.Diagnostics.CodeAnalysis.AllowNullAttribute") ||
                        customAttrs.Has("System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute") ||
                        customAttrs.Has("JetBrains.Annotations.CanBeNullAttribute"))
                    {
                        return NullabilityStatus.Null;
                    }
                }

                return NullabilityStatus.Oblivious;
            }
        }

        protected abstract class TypeRef : StructurallyEquatable
        {
            public abstract IList<Type> GetDependencies();
            public abstract string Render(Func<Type, string> typeScriptNameCalculator);
            public abstract bool IsAtomic { get; }

            public virtual bool Is(TypeRef typeRef) => Equals(typeRef);
            public virtual TypeRef Subtract(TypeRef typeRef) => Equals(typeRef) ? Never : this;

            class UserTypeRef : TypeRef
            {
                private readonly Type type;

                public UserTypeRef(Type type) => this.type = type;

                public override IList<Type> GetDependencies() => new[] {type.IsGenericType ? type.GetGenericTypeDefinition() : type};

                public override string Render(Func<Type, string> typeScriptNameCalculator) => typeScriptNameCalculator(this.type);

                public override bool IsAtomic => true;

                protected override IEnumerable<object> EqualsBy() => new[] { this.type };
            }

            class CompoundTypeRef : TypeRef
            {
                private readonly FormattableString formattableString;
                private readonly bool needsAtomicConstituents;

                public CompoundTypeRef(FormattableString formattableString, bool isAtomic, bool needsAtomicConstituents)
                {
                    this.formattableString = formattableString;
                    IsAtomic = isAtomic;
                    this.needsAtomicConstituents = needsAtomicConstituents;
                }

                public override IList<Type> GetDependencies()
                {
                    static IList<Type> DoGetReferencedTypes(FormattableString formattableString)
                    {
                        return formattableString
                            .GetArguments()
                            .SelectMany(p => p is TypeRef tr ? tr.GetDependencies() :
                                             p is FormattableString fs ? DoGetReferencedTypes(fs) :
                                             new Type[0])
                            .ToList();
                    }

                    return DoGetReferencedTypes(this.formattableString);
                }

                public override string Render(Func<Type, string> typeScriptNameCalculator)
                {
                    string RenderTypeRef(TypeRef typeRef)
                    {
                        var result = typeRef.Render(typeScriptNameCalculator);

                        return this.needsAtomicConstituents && !typeRef.IsAtomic ? $"({result})" : result;
                    }

                    string RenderFormattableString(FormattableString formattableString)
                    {
                        var formatArgs = formattableString
                            .GetArguments()
                            .Select(p => p is TypeRef tr ? RenderTypeRef(tr) :
                                         p is FormattableString fs ? RenderFormattableString(fs) :
                                         p?.ToString() ?? "")
                            .ToArray();

                        return string.Format(formattableString.Format, formatArgs);
                    }

                    return RenderFormattableString(this.formattableString);
                }

                public override bool IsAtomic { get; }

                protected override IEnumerable<object> EqualsBy() => this.formattableString.GetArguments().Append(this.formattableString.Format);
            }

            class UnionTypeRef: TypeRef
            {
                private readonly ISet<TypeRef> unionTypes;

                public UnionTypeRef(params TypeRef[] unionTypes)
                {
                    this.unionTypes = unionTypes.SelectMany(ExtractUnionTypes).ToHashSet();
                }

                public override IList<Type> GetDependencies() => this.unionTypes.SelectMany(ut => ut.GetDependencies()).ToList();

                public override string Render(Func<Type, string> typeScriptNameCalculator)
                {
                    if (this.unionTypes.Count == 0)
                        return "never";

                    return this.unionTypes.Select(ut => ut.Render(typeScriptNameCalculator)).JoinBy(" | ");
                }

                public override bool Is(TypeRef typeRef)
                {
                    var target = ExtractUnionTypes(typeRef);
                    return this.unionTypes.IsSupersetOf(target);
                }

                public override TypeRef Subtract(TypeRef typeRef)
                {
                    var target = ExtractUnionTypes(typeRef);
                    return new UnionTypeRef(this.unionTypes.Except(target).ToArray());
                }

                public override bool IsAtomic => false;

                protected override IEnumerable<object> EqualsBy() => this.unionTypes;

                private ISet<TypeRef> ExtractUnionTypes(TypeRef typeRef)
                {
                    if (typeRef is UnionTypeRef utr)
                        return utr.unionTypes;

                    return new[] { typeRef }.ToHashSet();
                }
            }

            public static TypeRef Compound(FormattableString typeRef, bool isAtomic = false, bool needsAtomicConstituents = true) => new CompoundTypeRef(typeRef, isAtomic, needsAtomicConstituents);
            public static TypeRef BuiltIn(string typeScriptName) => new CompoundTypeRef($"{typeScriptName}", isAtomic: true, needsAtomicConstituents: false);

            public static TypeRef Any => BuiltIn("any");
            public static TypeRef Null => BuiltIn("null");
            public static TypeRef Undefined => BuiltIn("undefined");
            public static TypeRef Never => Union();
            public static TypeRef Unknown => BuiltIn("unknown");
            public static TypeRef Boolean => BuiltIn("boolean");
            public static TypeRef Void => BuiltIn("void");
            public static TypeRef String => BuiltIn("string");
            public static TypeRef Number => BuiltIn("number");
            public static TypeRef FormData => BuiltIn("FormData");
            public static TypeRef Date => BuiltIn("Date");

            public static TypeRef UserType(Type type) => new UserTypeRef(type);
            public static TypeRef Union(params TypeRef[] types) => new UnionTypeRef(types);
            public static TypeRef GenericArg(Type type) => BuiltIn(type.Name);
            public static TypeRef GenericType(TypeRef type, IList<TypeRef> genericArguments) => Compound($"{type}<{genericArguments.SelectFS(arg => $"{arg}").JoinBy($", ")}>", isAtomic: true, needsAtomicConstituents: false);
            public static TypeRef ArrayOf(TypeRef element) => Compound($"{element}[]");
            public static TypeRef Dict(TypeRef key, TypeRef value) => Compound($"{{[key: {key}]: {value}}}", isAtomic: true, needsAtomicConstituents: false);
            public static TypeRef MappedType(TypeRef key, TypeRef value) => Compound($"{{[K in {key}]: {value}}}", isAtomic: true, needsAtomicConstituents: false);
        }

        protected static class DefaultModules
        {
            public const string Enums = "enums";
            public const string Dto = "dto";
            public const string Api = "api";
        }
    }
}
