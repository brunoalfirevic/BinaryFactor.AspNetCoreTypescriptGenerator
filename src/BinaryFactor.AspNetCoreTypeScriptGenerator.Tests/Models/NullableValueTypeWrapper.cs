// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models
{
    public class NullableValueTypeWrapper<T>
        where T: struct
    {
        public T? NullableValue { get; set; }

        public T NotNullValue { get; set; }
    }
}