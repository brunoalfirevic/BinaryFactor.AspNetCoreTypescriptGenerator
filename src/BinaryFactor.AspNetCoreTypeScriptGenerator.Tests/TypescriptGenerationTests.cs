using System.Linq;
using System.Reflection;
using Shouldly;
using BinaryFactor.Utilities;
using BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Utilities;
using System.IO;
using System;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests
{
    public class TypescriptGenerationTests
    {
        public void TypeScriptGenerationTest()
        {
            var destination = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\..\..\GeneratedCode");

            var generatedModules = new TypescriptGenerator()
                .GenerateCodeAndSave(destination)
                .ToDictionary(gm => gm.module.ModuleName, gm => gm.code);

            generatedModules.Count.ShouldBe(3);

            generatedModules["enums"].ShouldBeCode(GetExpectedCode("enums"));
            generatedModules["dto"].ShouldBeCode(GetExpectedCode("dto"));
            generatedModules["api"].ShouldBeCode(GetExpectedCode("api"));
        }

        private string GetExpectedCode(string moduleName)
        {
            return Assembly.GetExecutingAssembly().GetEmbeddedResourceTextByPath($@"ExpectedGeneratedCode\{moduleName}.ts");
        }
    }
}
