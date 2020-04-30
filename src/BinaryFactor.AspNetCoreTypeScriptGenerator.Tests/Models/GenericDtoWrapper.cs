// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models
{
    public class GenericDtoWrapper<K, V>
    {
        public K Key{ get; set; }
        public V Value { get; set; }
    }

    public class NullableValueTypeWrapper<T>
        where T: struct
    {
        public T? Value { get; set; }
    }
}
