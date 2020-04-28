﻿// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator
{
    public class TypeScriptGeneratorOptions
    {
        public bool StringsAreNullableByDefault { get; set; } = true;
        public bool UseUndefinedForNullableTypes { get; set; } = true;
        public IList<Type> AdditionalEntryTypes { get; set; } = new List<Type>();
        public IList<Assembly> EntryAssemblies { get; set; } = new[] { Assembly.GetEntryAssembly() };
        public Func<string, IEnumerable<FormattableString>> ModuleImports { get; set; } = _ => new FormattableString[] { };
        public Func<string, IEnumerable<FormattableString>> AdditionalModuleContent { get; set; } = _ => new FormattableString[] { };
        public Action<string> Logger { get; set; } = msg => Console.WriteLine(msg);
        public FormattableString Header { get; set; } = $"// This file is autogenerated, any manual changes will be lost after regeneration";
        public Func<Type, bool?> IsOwnTypeChecker { get; set; }
        public Func<string, FormattableString> RequestUrlExpression { get; set; }
    }
}
