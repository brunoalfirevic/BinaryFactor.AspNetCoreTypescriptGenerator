using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

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

        public class UserDto
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }
    }
}
