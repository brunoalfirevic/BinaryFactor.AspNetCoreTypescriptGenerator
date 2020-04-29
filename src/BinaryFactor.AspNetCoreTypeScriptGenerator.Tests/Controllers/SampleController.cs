using BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Controllers
{
    public enum UserType
    {
        Regular,
        Admin
    }

    [ApiController]
    [Route("[controller]/[action]")]
    public class SampleController: Controller
    {
        public IList<UserDto> GetRegisteredUsers(UserType userType) => default;

        public IList<UserDto> GetRegisteredUsersWithNullableParam(UserType? userType) => default;

        public IList<int?> GetIntegers() => default;

        public GenericDtoWrapper<string, UserDto> GetUserDtoWithWrapper() => default;

        public GenericDtoWrapper<int, IList<string>> GetIntWithWrapper() => default;

        public NullableValueTypeWrapper<DateTime> GetWrappedDateTime() => default;

        [return: MaybeNull]
        public DtoInModelsFolder GetMaybeNullReturn([AllowNull] string str) => default;

        public Dictionary<int, string> GetNumberDictionary() => default;

        public Dictionary<UserType, string> GetEnumDictionary() => default;

        [return: MaybeNull]
        public object GetMaybeNullObjectReturn([AllowNull] int number) => default;

        public class UserDto
        {
            [MaybeNull]
            public DtoInModelsFolder ValueNullableByMaybeNull { get; set; }

            public string FirstName { get; set; }

            [NotNull]
            public string LastNameNotNull { get; set; }
        }
    }
}
