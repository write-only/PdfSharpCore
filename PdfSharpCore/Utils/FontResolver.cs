
using System.Linq;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;

using PdfSharpCore.Internal;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;

using SixLabors.Fonts;


namespace PdfSharpCore.Utils
{


    public class FontResolver 
        : IFontResolver
    {
        public string DefaultFontName => "Arial";

        private static readonly Dictionary<string, FontFamilyModel> InstalledFonts = new Dictionary<string, FontFamilyModel>();

        private static readonly string[] SSupportedFonts;

        public FontResolver()
        {
        }

        static FontResolver()
        {
            string fontDir;

            bool isOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            if (isOSX)
            {
                fontDir = "/Library/Fonts/";
                SSupportedFonts = System.IO.Directory.GetFiles(fontDir, "*.ttf", System.IO.SearchOption.AllDirectories);
                SetupFontsFiles(SSupportedFonts);
                return;
            }

            bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            if (isLinux)
            {
                SSupportedFonts = LinuxSystemFontResolver.Resolve();
                SetupFontsFiles(SSupportedFonts);
                return;
            }

            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            if (isWindows)
            {
                fontDir = System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts");
                var fontPaths = new List<string>();

                fontPaths.AddRange(System.IO.Directory.GetFiles(fontDir, "*.ttf", System.IO.SearchOption.AllDirectories));
                fontPaths.AddRange(System.IO.Directory.GetFiles(fontDir, "*.ttc", System.IO.SearchOption.AllDirectories));

                var appdataFontDir = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts");
                if(System.IO.Directory.Exists(appdataFontDir))
                {
                    fontPaths.AddRange(System.IO.Directory.GetFiles(appdataFontDir, "*.ttf", System.IO.SearchOption.AllDirectories));
                    fontPaths.AddRange(System.IO.Directory.GetFiles(appdataFontDir, "*.ttc", System.IO.SearchOption.AllDirectories));
                }

                SSupportedFonts = fontPaths.ToArray();
                SetupFontsFiles(SSupportedFonts);
                return;
            }

            throw new System.NotImplementedException("FontResolver not implemented for this platform (PdfSharpCore.Utils.FontResolver.cs).");
        }


        internal readonly struct FontFileInfo
        {
            public FontFileInfo(string path, FontDescription fontDescription)
            {
                this.Path = path;
                this.FontDescription = fontDescription;
            }

            public string Path { get; }

            public FontDescription FontDescription { get; }

            public string FamilyName => this.FontDescription.FontFamilyInvariantCulture;


            public XFontStyle GuessFontStyle()
            {
                switch (this.FontDescription.Style)
                {
                    case FontStyle.Bold:
                        return XFontStyle.Bold;
                    case FontStyle.Italic:
                        return XFontStyle.Italic;
                    case FontStyle.BoldItalic:
                        return XFontStyle.BoldItalic;
                    default:
                        return XFontStyle.Regular;
                }
            }
        }


        public static void SetupFontsFiles(string[] sSupportedFonts)
        {
            List<FontFileInfo> tempFontInfoList = new List<FontFileInfo>();
            foreach (string fontPathFile in sSupportedFonts)
            {
                var fontDescriptions = new List<FontDescription>();
                if (fontPathFile.EndsWith(".ttc", System.StringComparison.InvariantCultureIgnoreCase))
                {
                    fontDescriptions.AddRange(FontDescription.LoadFontCollectionDescriptions(fontPathFile));
                }
                else if(fontPathFile.EndsWith(".ttf", System.StringComparison.InvariantCultureIgnoreCase))
                {
                    fontDescriptions.Add(FontDescription.LoadDescription(fontPathFile));
                }
                foreach (var fontDescription in fontDescriptions)
                {
                    try
                    {
                        FontFileInfo fontInfo = new FontFileInfo(fontPathFile, fontDescription);
                        Debug.WriteLine(fontPathFile);
                        tempFontInfoList.Add(fontInfo);
                    }
                    catch (System.Exception e)
                    {
                        System.Console.Error.WriteLine(e);
                    }
                }
            }

            // Deserialize all font families
            foreach (IGrouping<string, FontFileInfo> familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    string familyName = familyGroup.Key;
                    FontFamilyModel family = DeserializeFontFamily(familyName, familyGroup);
                    InstalledFonts.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
                    System.Console.Error.WriteLine(e);
                }
        }


        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            FontFamilyModel font = new FontFamilyModel { Name = fontFamilyName };

            // there is only one font
            if (fontList.Count() == 1)
                font.FontFiles.Add(XFontStyle.Regular, fontList.First().Path);
            else
            {
                foreach (FontFileInfo info in fontList)
                {
                    XFontStyle style = info.GuessFontStyle();
                    if (!font.FontFiles.ContainsKey(style))
                        font.FontFiles.Add(style, info.Path);
                }
            }

            return font;
        }

        public virtual byte[] GetFont(string faceFileName)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                string ttfPathFile = "";
                try
                {
                    ttfPathFile = SSupportedFonts.ToList().First(x => x.ToLower().Contains(
                        System.IO.Path.GetFileName(faceFileName).ToLower())
                    );

                    using (System.IO.Stream ttf = System.IO.File.OpenRead(ttfPathFile))
                    {
                        ttf.CopyTo(ms);
                        ms.Position = 0;
                        return ms.ToArray();
                    }
                }
                catch (System.Exception e)
                {
                    System.Console.WriteLine(e);
                    throw new System.Exception("No Font File Found - " + faceFileName + " - " + ttfPathFile);
                }
            }
        }

        public bool NullIfFontNotFound { get; set; } = false;

        public virtual FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (InstalledFonts.Count == 0)
                throw new System.IO.FileNotFoundException("No Fonts installed on this device!");

            if (InstalledFonts.TryGetValue(familyName.ToLower(), out FontFamilyModel family))
            {
                if (isBold && isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.BoldItalic, out string boldItalicFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(boldItalicFile));
                }
                else if (isBold)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Bold, out string boldFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(boldFile));
                }
                else if (isItalic)
                {
                    if (family.FontFiles.TryGetValue(XFontStyle.Italic, out string italicFile))
                        return new FontResolverInfo(System.IO.Path.GetFileName(italicFile));
                }

                if (family.FontFiles.TryGetValue(XFontStyle.Regular, out string regularFile))
                    return new FontResolverInfo(System.IO.Path.GetFileName(regularFile));

                return new FontResolverInfo(System.IO.Path.GetFileName(family.FontFiles.First().Value));
            }

            if (NullIfFontNotFound)
                return null;

            string ttfFile = InstalledFonts.First().Value.FontFiles.First().Value;
            return new FontResolverInfo(System.IO.Path.GetFileName(ttfFile));
        }
    }
}
