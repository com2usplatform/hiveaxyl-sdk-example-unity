// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System;

namespace Hive.Axyl.Samples.Recipes
{
    /// <summary>
    /// Decodes base64url (RFC 4648 §5).
    /// </summary>
    public static class Base64Url
    {
        /// <summary>
        /// Decodes a base64url string to its bytes, restoring the padding the encoding omits.
        /// </summary>
        /// <exception cref="FormatException">Thrown when the input is not valid base64url.</exception>
        public static byte[] Decode(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: throw new FormatException("Invalid base64url length.");
            }

            return Convert.FromBase64String(s);
        }
    }
}
