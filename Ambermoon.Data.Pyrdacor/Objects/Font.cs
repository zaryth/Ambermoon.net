﻿using Ambermoon.Data.Legacy.Serialization;
using Ambermoon.Data.Serialization;

namespace Ambermoon.Data.Pyrdacor.Objects
{
    internal class Font
    {
        readonly List<Glyph> glyphs;
        
        public bool Monospace { get; }

        public Font(IDataReader dataReader)
        {
            // If 0, this is no monospace font and the width and advance
            // are given as 1 or 2 header bytes for each glyph.
            int glyphWidth = dataReader.ReadByte();
            int glyphHeight = dataReader.ReadByte();
            int advance = glyphWidth;
            int count = dataReader.ReadWord();

            glyphs = new List<Glyph>(count);

            if (glyphWidth == 0) // regular font
            {
                Monospace = false;

                for (int i = 0; i < count; ++i)
                {
                    glyphWidth = dataReader.ReadByte();

                    if ((glyphWidth & 0x80) != 0)
                    {
                        glyphWidth &= 0x7f;
                        advance = dataReader.ReadByte();
                    }
                    else
                    {
                        advance = glyphWidth;
                    }

                    glyphs.Add(LoadGlyph());
                }
            }
            else // monospace font
            {
                Monospace = true;

                for (int i = 0; i < count; ++i)
                    glyphs.Add(LoadGlyph());
            }

            Glyph LoadGlyph()
            {
                var graphic = new Graphic
                {
                    Width = glyphWidth,
                    Height = glyphHeight,
                    Data = new byte[glyphWidth * glyphHeight],
                    IndexedGraphic = true
                };

                for (int y = 0; y < glyphHeight; ++y)
                {
                    var lineBytes = dataReader.ReadBytes((glyphWidth + 7) / 8);

                    for (int l = 0; l < lineBytes.Length; ++l)
                    {
                        byte line = lineBytes[l];
                        int lineSize = l < lineBytes.Length - 1 ? 8 : glyphWidth % 8;

                        for (int x = 0; x < lineSize; ++x)
                        {
                            graphic.Data[l * 8 + x + y * glyphWidth] = (byte)((line & 0x80) >> 7);
                            line <<= 1;
                        }
                    }
                }

                return new Glyph { Graphic = graphic, Advance = (byte)advance };
            }
        }

        public void Write(IDataWriter dataWriter)
        {
            if (glyphs.Count == 0)
            {
                dataWriter.Write(0u);
                return;
            }

            void ValidateGlyph(Glyph glyph)
            {
                if (glyph.Graphic == null ||
                    glyph.Graphic.Width < 1 || glyph.Graphic.Width > 127 ||
                    glyph.Graphic.Height < 1 || glyph.Graphic.Height > 255 ||
                    glyph.Advance < 0 || glyph.Advance > 255)
                    throw new AmbermoonException(ExceptionScope.Data, "Invalid glyph.");
            }

            dataWriter.Write((byte)(Monospace ? glyphs[0].Graphic.Width : 0));
            dataWriter.Write((byte)glyphs[0].Graphic.Height);
            dataWriter.Write((ushort)glyphs.Count);

            if (Monospace)
            {
                foreach (var glyph in glyphs)
                {
                    ValidateGlyph(glyph);
                    SaveGlyph(glyph);
                }
            }
            else
            {
                foreach (var glyph in glyphs)
                {
                    ValidateGlyph(glyph);

                    byte width = (byte)glyph.Graphic.Width;

                    if (glyph.Advance != glyph.Graphic.Width)
                    {
                        width |= 0x80;
                        dataWriter.Write(width);
                        dataWriter.Write((byte)glyph.Advance);
                    }
                    else
                    {
                        dataWriter.Write(width);
                    }

                    SaveGlyph(glyph);
                }
            }

            void SaveGlyph(Glyph glyph)
            {
                var lineBytes = new byte[(glyph.Graphic.Width + 7) / 8];

                for (int y = 0; y < glyph.Graphic.Height; ++y)
                {
                    for (int l = 0; l < lineBytes.Length; ++l)
                    {
                        byte line = 0;
                        int lineSize = l < lineBytes.Length - 1 ? 8 : glyph.Graphic.Width % 8;
                        byte mask = 0x80;

                        for (int x = 0; x < lineSize; ++x)
                        {
                            if (glyph.Graphic.Data[l * 8 + x + y * glyph.Graphic.Width] != 0)
                                line |= mask;
                            mask >>= 1;
                        }

                        lineBytes[l] = line;
                    }

                    dataWriter.Write(lineBytes);
                }
            }
        }

        public Graphic GetGlyphGraphic(uint glyphIndex) => glyphs[(int)glyphIndex].Graphic;
        public Glyph GetGlyph(uint glyphIndex) => glyphs[(int)glyphIndex];
    }
}
