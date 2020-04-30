// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models
{
    public class NullableReferenceTypeWrapper<T>
       where T : class
    {
        [MaybeNull]
        public T MaybeNullValue { get; set; }

        public T NotNullValue { get; set; }
    }
}
