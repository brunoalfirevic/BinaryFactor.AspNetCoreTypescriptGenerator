// Copyright (c) Bruno Alfirević. All rights reserved.
// Licensed under the MIT license. See license.txt in the project root for license information.

using System.Text.RegularExpressions;

namespace BinaryFactor.AspNetCoreTypeScriptGenerator
{
    static class StringExtensions
    {
        public static string ReplaceIgnoreCase(this string str, string oldValue, string newValue)
        {
            return Regex.Replace(str, Regex.Escape(oldValue), newValue.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }
    }
}
