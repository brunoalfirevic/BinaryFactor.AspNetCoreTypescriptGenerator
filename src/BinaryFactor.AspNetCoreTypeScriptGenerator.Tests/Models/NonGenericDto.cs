﻿// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models
{
    public class NonGenericDto
    {
        public string Value;

        public int PrivateGetterProperty { private get; set; }
    }
}
