using System;
using System.Security.Cryptography;

namespace Walkabout.Data
{
    public static class DataEnginePasswordGenerator
    {
        private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";
        private const string Digits = "23456789";
        private const string Symbols = "!@#$%^&*-_=+";

        public static string Generate(int length = 24)
        {
            if (length < 8)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Password length must be at least 8 characters.");
            }

            string allChars = Uppercase + Lowercase + Digits + Symbols;
            char[] result = new char[length];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                result[0] = PickRandomChar(rng, Uppercase);
                result[1] = PickRandomChar(rng, Lowercase);
                result[2] = PickRandomChar(rng, Digits);
                result[3] = PickRandomChar(rng, Symbols);

                for (int i = 4; i < length; i++)
                {
                    result[i] = PickRandomChar(rng, allChars);
                }

                Shuffle(rng, result);
            }

            return new string(result);
        }

        private static char PickRandomChar(RandomNumberGenerator rng, string charset)
        {
            byte[] buffer = new byte[4];
            rng.GetBytes(buffer);
            uint value = BitConverter.ToUInt32(buffer, 0);
            int index = (int)(value % (uint)charset.Length);
            return charset[index];
        }

        private static void Shuffle(RandomNumberGenerator rng, char[] chars)
        {
            for (int i = chars.Length - 1; i > 0; i--)
            {
                byte[] buffer = new byte[4];
                rng.GetBytes(buffer);
                uint value = BitConverter.ToUInt32(buffer, 0);
                int j = (int)(value % (uint)(i + 1));
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
        }
    }
}
