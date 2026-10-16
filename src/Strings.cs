using System.Globalization;

namespace Stickers
{
    // Interface language: Russian or English. "auto" follows the Windows display language.
    public static class L
    {
        public static bool En { get; private set; }

        public static void Init(string preference)
        {
            if (preference == "en") En = true;
            else if (preference == "ru") En = false;
            else En = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru";
        }

        public static string T(string ru, string en)
        {
            return En ? en : ru;
        }
    }
}
