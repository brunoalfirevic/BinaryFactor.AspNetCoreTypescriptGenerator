using BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models;
using Microsoft.AspNetCore.Mvc;
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
        public IList<UserDto> GetRegisteredUsers(UserType userType)
        {
            return new List<UserDto>();
        }

        [return: MaybeNull]
        public DtoInModelsFolder GetMaybeNullReturn([AllowNull] int number)
        {
            return null;
        }

        public class UserDto
        {
            [MaybeNull]
            public DtoInModelsFolder ValueNullableByMaybeNull { get; set; }

            public string FirstName { get; set; }
        }
    }

    public class MyCustomAttr: System.Attribute
    {
    }
}
