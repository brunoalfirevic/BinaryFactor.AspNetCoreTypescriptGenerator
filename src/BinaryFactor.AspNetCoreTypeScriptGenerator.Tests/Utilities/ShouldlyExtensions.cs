// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System;
using System.Linq;
using BinaryFactor.Utilities;
using Shouldly;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Utilities
{
    [ShouldlyMethods]
    public static class ShouldlyExtensions
    {
        public static void ShouldBeCode(this string code, string expectedCode)
        {
            static string Tidy(string str)
            {
                return str
                    .SplitLines()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .JoinBy(Environment.NewLine);
            }

            Tidy(code).ShouldBe(Tidy(expectedCode));
        }
    }
}
