// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class SampleController: Controller
    {
        public IList<NestedDto> GetNestedDtos(EnumType enumParameter) => default;

        public IList<NestedDto> GetNestedDtosWithNullableParam(EnumType? nullableEnumParameter) => default;

        public IList<int?> GetIntegers() => default;

        public GenericDto<string, NestedDto> GetGenericDto() => default;

        public GenericDto<int, IList<string>> GetGenericDtoWithList() => default;

        public NullableValueTypeWrapper<DateTime> GetWrappedDateTime() => default;

        public NullableReferenceTypeWrapper<string> GetWrappedString() => default;

        [return: MaybeNull]
        public NonGenericDto GetMaybeNullReturn([AllowNull] string str) => default;

        [HttpPost]
        public void ReceiveDtoWithAllowNull([AllowNull] NonGenericDto nonGenericDto) { }

        public Dictionary<int, string> GetNumberDictionary() => default;

        public IDictionary<string, string> GetStringDictionary() => default;

        public Dictionary<EnumType, bool> GetEnumDictionary() => default;

        [return: MaybeNull]
        public object GetMaybeNullObjectReturn(int? number) => default;

        [HttpPost]
        public object PostWithStringParam([FromForm] string stringParam, [FromForm] string intParam) => default;

        public class NestedDto
        {
            [MaybeNull]
            public NonGenericDto ValueNullableByMaybeNull { get; set; }

            public string FirstNameNullableByStringRule { get; set; }

            [NotNull]
            public string LastNameNotNullByNotNullAttribute { get; set; }

            [DisallowNull]
            public string LastNameNotNullByDisallowNullAttribute { get; set; }
        }
    }
}
