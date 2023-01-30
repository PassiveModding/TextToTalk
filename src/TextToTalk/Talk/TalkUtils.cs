﻿using Dalamud.Data;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TextToTalk.Middleware;

namespace TextToTalk.Talk
{
    public static class TalkUtils
    {
        private static readonly Regex Speakable = new(@"\p{L}+|\p{M}+|\p{N}+", RegexOptions.Compiled);
        private static readonly Regex Stutter = new(@"(?<=\s|^)\p{L}{1,2}-", RegexOptions.Compiled);
        private static readonly Regex Bracketed = new(@"<[^<]*>", RegexOptions.Compiled);

        public static unsafe TalkAddonText ReadTalkAddon(DataManager data, AddonTalk* talkAddon)
        {
            return new TalkAddonText
            {
                Speaker = ReadTextNode(talkAddon->AtkTextNode220),
                Text = ReadTextNode(talkAddon->AtkTextNode228),
            };
        }

        public static unsafe TalkAddonText ReadTalkAddon(DataManager data, AddonBattleTalk* talkAddon)
        {
            return new TalkAddonText
            {
                Speaker = ReadTextNode(talkAddon->AtkTextNode220),
                Text = ReadTextNode(talkAddon->AtkTextNode228),
            };
        }

        private static unsafe string ReadTextNode(AtkTextNode* textNode)
        {
            if (textNode == null) return "";

            var textPtr = textNode->NodeText.StringPtr;
            var textLength = textNode->NodeText.BufUsed - 1; // Null-terminated; chop off the null byte
            if (textLength is <= 0 or > int.MaxValue) return "";

            var textBytes = new byte[textLength];
            Marshal.Copy((nint)textPtr, textBytes, 0, (int)textLength);
            var seString = SeString.Parse(textBytes);
            return seString.TextValue
                .Trim()
                .Replace("\n", "")
                .Replace("\r", "");
        }

        public static unsafe bool IsVisible(AddonTalk* talkAddon)
        {
            return talkAddon == null || talkAddon->AtkUnitBase.IsVisible;
        }

        public static unsafe bool IsVisible(AddonBattleTalk* talkAddon)
        {
            return talkAddon == null || talkAddon->AtkUnitBase.IsVisible;
        }

        public static string StripAngleBracketedText(string text)
        {
            // TextToTalk#17 "<sigh>"
            return Bracketed.Replace(text, "");
        }

        public static string ReplaceSsmlTokens(string text)
        {
            return text.Replace("&", "and");
        }

        public static string NormalizePunctuation(string text)
        {
            return text
                // TextToTalk#29 emdashes and dashes and whatever else
                .Replace("─", " - ") // These are not the same character
                .Replace("—", " - ")
                .Replace("–",
                    "-"); // Hopefully, this one is only in Kan-E-Senna's name? Otherwise, I'm not sure how to parse this correctly.
        }

        /// <summary>
        /// Removes single letters with a hyphen following them, since they aren't read as expected.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <returns>The cleaned text.</returns>
        public static string RemoveStutters(string text)
        {
            while (true)
            {
                if (!Stutter.IsMatch(text)) return text;
                text = Stutter.Replace(text, "");
            }
        }

        public static bool IsSpeakable(string text)
        {
            // TextToTalk#41 Unspeakable text
            return Speakable.Match(text).Success;
        }

        public static string GetPartialName(string name, FirstOrLastName part)
        {
            var names = name.Split(' ');

            switch (part)
            {
                case FirstOrLastName.First:
                    return names[0];
                case FirstOrLastName.Last:
                    if (names.Length == 1)
                        return names[0]; // Some NPCs only have one name.
                    return names[1];
                default:
                    throw new ArgumentOutOfRangeException(nameof(part), part, "Enumeration value is out of range.");
            }
        }
    }
}