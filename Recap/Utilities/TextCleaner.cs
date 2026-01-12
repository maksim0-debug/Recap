using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Recap.Utilities
{
    public static class TextCleaner
    {
        private static readonly HashSet<char> Vowels = new HashSet<char>(new[] {
            'a', 'e', 'i', 'o', 'u', 'y',
            'A', 'E', 'I', 'O', 'U', 'Y',
            'а', 'е', 'ё', 'и', 'о', 'у', 'ы', 'э', 'ю', 'я',
            'А', 'Е', 'Ё', 'И', 'О', 'У', 'Ы', 'Э', 'Ю', 'Я'
        });

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder(text.Length);

            foreach (var word in words)
            {
                var trimmed = word.Trim();
                if (trimmed.Length == 0) continue;

                if (PassesFilters(trimmed))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(trimmed);
                }
            }

            return sb.ToString();
        }

        private static bool PassesFilters(string word)
        {
            if (IsGarbageLength(word)) return false;
            if (IsHighSymbolRatio(word)) return false;
            if (IsRepeatedChar(word)) return false;
            if (HasNoVowels(word)) return false;
            if (IsMixedScript(word)) return false;
            if (IsEntropyGarbage(word)) return false;

            return true;
        }

        public static bool IsGarbageLength(string word)
        {
            if (word.Length >= 2) return false;
            char c = word[0];
            return !char.IsDigit(c) && "aiAI".IndexOf(c) == -1;
        }

        public static bool IsHighSymbolRatio(string word)
        {
            int alphanum = 0;
            foreach (char c in word)
            {
                if (char.IsLetterOrDigit(c)) alphanum++;
            }
            return (double)alphanum / word.Length < 0.5;
        }

        public static bool IsRepeatedChar(string word)
        {
            if (word.Length <= 3) return false;
            int repeat = 1;
            for (int i = 1; i < word.Length; i++)
            {
                if (word[i] == word[i - 1]) repeat++;
                else repeat = 1;
                if (repeat > 3) return true;
            }
            return false;
        }

        public static bool HasNoVowels(string word)
        {
            if (word.Length <= 4) return false;
            bool hasVowel = false;
            bool allCaps = true;
            foreach (char c in word)
            {
                if (Vowels.Contains(c)) hasVowel = true;
                if (char.IsLetter(c) && !char.IsUpper(c)) allCaps = false;
            }
            if (allCaps) return false;
            return !hasVowel;
        }

        public static bool IsMixedScript(string word)
        {
            bool hasLatin = false;
            bool hasCyrillic = false;

            foreach (var c in word)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLatin = true;
                else if (c >= 0x0400 && c <= 0x04FF) hasCyrillic = true;

                if (hasLatin && hasCyrillic) return true;
            }
            return false;
        }

        public static bool IsEntropyGarbage(string word)
        {
            if (word.Length < 3) return false;

            var freqs = new Dictionary<char, int>();
            foreach (char c in word)
            {
                if (!freqs.ContainsKey(c)) freqs[c] = 0;
                freqs[c]++;
            }

            double entropy = 0;
            double len = word.Length;
            foreach (var val in freqs.Values)
            {
                double p = val / len;
                entropy -= p * Math.Log(p, 2);
            }

            if (word.Length < 15 && entropy > 4.5) return true;

            return false;
        }
    }
}
