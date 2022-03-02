using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace XPressions
{
    public class tk2dSpriteCollectionBuilder
    {
        class SpriteLut
        {
            public int
                source; // index into source texture list, will only have multiple entries with same source, when splitting

            public Texture2D sourceTex;
            public Texture2D tex; // texture to atlas

            public bool isSplit; // is this part of a split?
            public int rx, ry, rw, rh; // split rectangle in texture coords

            public bool isDuplicate; // is this a duplicate texture?
            public int atlasIndex; // index in the atlas
            public string hash; // hash of the tex data and rect

            public bool isFont;
            public int fontId;
            public int charId;
        }

        static int defaultPad = 2;

        static int GetPadAmount(tk2dSpriteCollection gen, int spriteId)
        {
            int basePadAmount = 0;

            if (gen.padAmount == -1) basePadAmount = (gen.filterMode == FilterMode.Point) ? 0 : defaultPad;
            else basePadAmount = gen.padAmount;

            if (spriteId >= 0)
                basePadAmount += (gen.textureParams[spriteId].extraPadding == -1)
                    ? 0
                    : gen.textureParams[spriteId].extraPadding;

            return Mathf.Max(0, basePadAmount);
        }

        static void PadTexture(Texture2D tex, int pad, tk2dSpriteCollectionDefinition.Pad padMode)
        {
            // Default is now extend
            if (padMode == tk2dSpriteCollectionDefinition.Pad.Default)
            {
                padMode = tk2dSpriteCollectionDefinition.Pad.Extend;
            }

            Color bgColor = new Color(0, 0, 0, 0);
            Color c0 = bgColor, c1 = bgColor;
            for (int y = 0; y < pad; ++y)
            {
                for (int x = 0; x < tex.width; ++x)
                {
                    switch (padMode)
                    {
                        case tk2dSpriteCollectionDefinition.Pad.Extend:
                            c0 = tex.GetPixel(x, pad);
                            c1 = tex.GetPixel(x, tex.height - 1 - pad);
                            break;
                        case tk2dSpriteCollectionDefinition.Pad.TileX:
                        case tk2dSpriteCollectionDefinition.Pad.TileXY:
                            c1 = tex.GetPixel(x, pad);
                            c0 = tex.GetPixel(x, tex.height - 1 - pad);
                            break;
                    }

                    tex.SetPixel(x, y, c0);
                    tex.SetPixel(x, tex.height - 1 - y, c1);
                }
            }

            for (int x = 0; x < pad; ++x)
            {
                for (int y = 0; y < tex.height; ++y)
                {
                    switch (padMode)
                    {
                        case tk2dSpriteCollectionDefinition.Pad.Extend:
                            c0 = tex.GetPixel(pad, y);
                            c1 = tex.GetPixel(tex.width - 1 - pad, y);
                            break;
                        case tk2dSpriteCollectionDefinition.Pad.TileY:
                        case tk2dSpriteCollectionDefinition.Pad.TileXY:
                            c1 = tex.GetPixel(pad, y);
                            c0 = tex.GetPixel(tex.width - 1 - pad, y);
                            break;
                    }

                    tex.SetPixel(x, y, c0);
                    tex.SetPixel(tex.width - 1 - x, y, c1);
                }
            }
        }



        static void SetUpSourceTextureFormats(tk2dSpriteCollection gen)
        {
            // make sure all textures are in the right format
            int numTexturesReimported = 0;
            List<Texture2D> texturesToProcess = new List<Texture2D>();

            for (int i = 0; i < gen.textureParams.Length; ++i)
            {
                if (gen.textureParams[i].texture != null)
                {
                    texturesToProcess.Add(gen.textureParams[i].texture);
                }
            }

            if (gen.spriteSheets != null)
            {
                foreach (var v in gen.spriteSheets)
                {
                    if (v.texture != null)
                        texturesToProcess.Add(v.texture);
                }
            }

            if (gen.fonts != null)
            {
                foreach (var v in gen.fonts)
                {
                    if (v.active && v.texture != null)
                        texturesToProcess.Add(v.texture);
                }
            }
        }

        static bool TextureRectFullySolid(Texture2D srcTex, int sx, int sy, int tw, int th)
        {
            for (int y = 0; y < th; ++y)
            {
                for (int x = 0; x < tw; ++x)
                {
                    Color32 col = srcTex.GetPixel(sx + x, sy + y);
                    if (col.a < 255)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        static Texture2D ProcessTexture(tk2dSpriteCollection settings, bool additive,
            tk2dSpriteCollectionDefinition.Pad padMode, bool disableTrimming, bool isInjectedTexture, bool isDiced,
            Texture2D srcTex, int sx, int sy, int tw, int th, ref SpriteLut spriteLut, int padAmount)
        {
            // Can't have additive without premultiplied alpha
            if (!settings.premultipliedAlpha) additive = false;
            bool allowTrimming = !settings.disableTrimming && !disableTrimming;
            var textureCompression = settings.textureCompression;

            int[] ww = new int[tw];
            int[] hh = new int[th];
            for (int x = 0; x < tw; ++x) ww[x] = 0;
            for (int y = 0; y < th; ++y) hh[y] = 0;
            int numNotTransparent = 0;
            for (int x = 0; x < tw; ++x)
            {
                for (int y = 0; y < th; ++y)
                {
                    Color col = srcTex.GetPixel(sx + x, sy + y);
                    if (col.a > 0)
                    {
                        ww[x] = 1;
                        hh[y] = 1;
                        numNotTransparent++;
                    }
                }
            }

            if (!allowTrimming || numNotTransparent > 0)
            {
                int x0 = 0, x1 = 0, y0 = 0, y1 = 0;

                bool customSpriteGeometry = false;
                if (!isInjectedTexture && settings.textureParams[spriteLut.source].customSpriteGeometry)
                {
                    customSpriteGeometry = true;
                }

                // For custom geometry, use the bounds of the geometry
                if (customSpriteGeometry)
                {
                    var textureParams = settings.textureParams[spriteLut.source];

                    x0 = int.MaxValue;
                    y0 = int.MaxValue;
                    x1 = -1;
                    y1 = -1;

                    foreach (var island in textureParams.geometryIslands)
                    {
                        foreach (Vector2 rawVert in island.points)
                        {
                            Vector2 vert = rawVert * settings.globalTextureRescale;
                            int minX = Mathf.FloorToInt(vert.x);
                            int maxX = Mathf.CeilToInt(vert.x);
                            float y = th - vert.y;
                            int minY = Mathf.FloorToInt(y);
                            int maxY = Mathf.CeilToInt(y);

                            x0 = Mathf.Min(x0, minX);
                            y0 = Mathf.Min(y0, minY);
                            x1 = Mathf.Max(x1, maxX);
                            y1 = Mathf.Max(y1, maxY);
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < tw; ++x)
                        if (ww[x] == 1)
                        {
                            x0 = x;
                            break;
                        }

                    for (int x = tw - 1; x >= 0; --x)
                        if (ww[x] == 1)
                        {
                            x1 = x;
                            break;
                        }

                    for (int y = 0; y < th; ++y)
                        if (hh[y] == 1)
                        {
                            y0 = y;
                            break;
                        }

                    for (int y = th - 1; y >= 0; --y)
                        if (hh[y] == 1)
                        {
                            y1 = y;
                            break;
                        }
                }

                x1 = Mathf.Min(x1, tw - 1);
                y1 = Mathf.Min(y1, th - 1);

                int w1 = x1 - x0 + 1;
                int h1 = y1 - y0 + 1;

                if (!allowTrimming)
                {
                    x0 = 0;
                    y0 = 0;
                    w1 = tw;
                    h1 = th;
                }

                Texture2D dtex = new Texture2D(w1 + padAmount * 2, h1 + padAmount * 2);
                dtex.hideFlags = HideFlags.DontSave;
                for (int x = 0; x < w1; ++x)
                {
                    for (int y = 0; y < h1; ++y)
                    {
                        Color col = srcTex.GetPixel(sx + x0 + x, sy + y0 + y);
                        dtex.SetPixel(x + padAmount, y + padAmount, col);
                    }
                }

                // Diced textures get padded differently - but should behave identically to legacy behaviour outside
                // the special padding regions
                if (isDiced)
                {
                    for (int y = 0; y < dtex.height; ++y)
                    {
                        for (int x = 0; x < dtex.width; ++x)
                        {
                            if (y >= padAmount && y < (h1 + padAmount) && x >= padAmount && x < (w1 + padAmount))
                            {
                                continue; // this is inefficient
                            }

                            int ox = sx + x0 + x - padAmount;
                            int oy = sy + y0 + y - padAmount;
                            // bool oob = ox < 0 || ox >= srcTex.width || oy < 0 || oy >= srcTex.height;
                            ox = Mathf.Clamp(ox, 0, srcTex.width - 1);
                            oy = Mathf.Clamp(oy, 0, srcTex.height - 1);
                            Color col = srcTex.GetPixel(ox, oy);
                            dtex.SetPixel(x, y, col);
                        }
                    }
                }

                if (settings.premultipliedAlpha)
                {
                    for (int x = 0; x < dtex.width; ++x)
                    {
                        for (int y = 0; y < dtex.height; ++y)
                        {
                            Color col = dtex.GetPixel(x, y);
                            col.r *= col.a;
                            col.g *= col.a;
                            col.b *= col.a;
                            col.a = additive ? 0.0f : col.a;
                            dtex.SetPixel(x, y, col);
                        }
                    }
                }

                if (!isDiced)
                {
                    PadTexture(dtex, padAmount, padMode);
                }

                dtex.Apply();

                spriteLut.rx = sx + x0;
                spriteLut.ry = sy + y0;
                spriteLut.rw = w1;
                spriteLut.rh = h1;
                spriteLut.tex = dtex;

                return dtex;
            }
            else
            {
                return null;
            }
        }

        static tk2dSpriteCollection currentBuild = null;
        static Texture2D[] sourceTextures;
        
        static bool CheckSourceAssets(tk2dSpriteCollection gen)
        {
            string missingTextures = "";

            foreach (var param in gen.textureParams)
            {
                if (param.texture == null && param.name.Length > 0)
                {
                    missingTextures += "  Missing texture: " + param.name;
                }
            }

            if (missingTextures.Length > 0)
            {
                XPressions.Instance.LogError(string.Format("Error in sprite collection '{0}'\n{1}", gen.name, missingTextures));
            }

            return missingTextures.Length == 0;
        }

        static void SetSpriteLutHash(SpriteLut lut)
        {
            byte[] buf;
            if (lut.tex)
            {
                Color32[] pixelData = lut.tex.GetPixels32();
                int ptr = 0;
                buf = new byte[6 + pixelData.Length * 4];
                for (int i = 0; i < pixelData.Length; ++i)
                {
                    buf[ptr++] = pixelData[i].r;
                    buf[ptr++] = pixelData[i].g;
                    buf[ptr++] = pixelData[i].b;
                    buf[ptr++] = pixelData[i].a;
                }

                buf[ptr++] = (byte)((lut.tex.width & 0x000000ff));
                buf[ptr++] = (byte)((lut.tex.width & 0x0000ff00) >> 8);
                buf[ptr++] = (byte)((lut.tex.width & 0x00ff0000) >> 16);
                buf[ptr++] = (byte)((lut.tex.height & 0x000000ff));
                buf[ptr++] = (byte)((lut.tex.height & 0x0000ff00) >> 8);
                buf[ptr++] = (byte)((lut.tex.height & 0x00ff0000) >> 16);
            }
            else
            {
                buf = new byte[] { 0 };
            }

            MD5 md5Hash = MD5.Create();
            byte[] data = md5Hash.ComputeHash(buf);
            StringBuilder sBuilder = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; ++i)
                sBuilder.Append(data[i].ToString("x2"));
            lut.hash = sBuilder.ToString();
        }

        public static bool Rebuild(tk2dSpriteCollection gen)
        {
            // avoid "recursive" build being triggered by texture watcher
            if (currentBuild != null)
                return false;
            
            currentBuild = gen;
            //gen.Upgrade(); // upgrade if necessary. could be invoked by texture watcher.

            // Check all source assets are present, fail otherwise
            //if (!CheckSourceAssets(gen))
            //{
            //    return false;
            //}
            
            // Make sure all source textures are in the correct format
            SetUpSourceTextureFormats(gen);

            // blank texture used when texture has been deleted
            Texture2D blankTexture = new Texture2D(2, 2);
            blankTexture.hideFlags = HideFlags.DontSave;
            blankTexture.SetPixel(0, 0, Color.magenta);
            blankTexture.SetPixel(0, 1, Color.yellow);
            blankTexture.SetPixel(1, 0, Color.cyan);
            blankTexture.SetPixel(1, 1, Color.grey);
            blankTexture.Apply();
            
            // make local texture sources
            List<Texture2D> allocatedTextures = new List<Texture2D>();
            allocatedTextures.Add(blankTexture);
            
            // If globalTextureRescale is 0.5 or 0.25, average pixels from the larger image. Otherwise just pick one pixel, and look really bad
            Texture2D[] rescaledTexs = null;
            if (gen.globalTextureRescale < 0.999f)
            {
                rescaledTexs = new Texture2D[gen.textureParams.Length];
                for (int i = 0; i < gen.textureParams.Length; ++i)
                {
                    if (gen.textureParams[i] != null
                        && !gen.textureParams[i].extractRegion
                        && gen.textureParams[i].texture != null)
                    {
                        rescaledTexs[i] = tk2dSpriteCollectionBuilderUtil.RescaleTexture(gen.textureParams[i].texture,
                            gen.globalTextureRescale);
                        allocatedTextures.Add(rescaledTexs[i]);
                    }
                }
            }
            else
            {
                gen.globalTextureRescale = 1;
            }
            
            Dictionary<Texture2D, Texture2D> extractRegionCache = new Dictionary<Texture2D, Texture2D>();
            sourceTextures = new Texture2D[gen.textureParams.Length];
            for (int i = 0; i < gen.textureParams.Length; ++i)
            {
                var param = gen.textureParams[i];

                if (param.extractRegion && param.texture != null)
                {
                    Texture2D srcTex = param.texture;
                    if (rescaledTexs != null)
                    {
                        if (!extractRegionCache.TryGetValue(param.texture, out srcTex))
                        {
                            srcTex = tk2dSpriteCollectionBuilderUtil.RescaleTexture(param.texture,
                                gen.globalTextureRescale);
                            extractRegionCache[param.texture] = srcTex;
                        }
                    }

                    int regionX = param.regionX;
                    int regionY = param.regionY;
                    int regionW = param.regionW;
                    int regionH = param.regionH;
                    if (rescaledTexs != null)
                    {
                        int k = tk2dSpriteCollectionBuilderUtil.NiceRescaleK(gen.globalTextureRescale);
                        int x2, y2;
                        if (k != 0)
                        {
                            regionX /= k;
                            regionY /= k;
                            x2 = regionX + (regionW + k - 1) / k;
                            y2 = regionY + (regionH + k - 1) / k;
                        }
                        else
                        {
                            x2 = (int)((regionX + regionW) * gen.globalTextureRescale);
                            y2 = (int)((regionY + regionH) * gen.globalTextureRescale);
                            regionX = (int)(regionX * gen.globalTextureRescale);
                            regionY = (int)(regionY * gen.globalTextureRescale);
                        }

                        regionW = Mathf.Min(x2, srcTex.width - 1) - regionX;
                        regionH = Mathf.Min(y2, srcTex.height - 1) - regionY;
                    }

                    Texture2D localTex = new Texture2D(regionW, regionH);
                    localTex.hideFlags = HideFlags.DontSave;
                    for (int y = 0; y < regionH; ++y)
                    {
                        for (int x = 0; x < regionW; ++x)
                        {
                            localTex.SetPixel(x, y, srcTex.GetPixel(regionX + x, regionY + y));
                        }
                    }
                    
                    if (Emoter.Data[0].entries[i].flipped)
                    {
                        Utility.UndoFlip(ref localTex);
                    }

                    localTex.name = $"tex_{i:D4}";
                    localTex.Apply();
                    allocatedTextures.Add(localTex);
                    sourceTextures[i] = localTex;
                }
                else
                {
                    sourceTextures[i] = (rescaledTexs != null) ? rescaledTexs[i] : param.texture;
                }
            }
            
            // Clear the region cache
            foreach (Texture2D tex in extractRegionCache.Values)
            {
                //Object.DestroyImmediate(tex);
            }

            extractRegionCache = null;
            
            // catalog all textures to atlas
            int numTexturesToAtlas = 0;
            List<SpriteLut> spriteLuts = new List<SpriteLut>();
            for (int i = 0; i < gen.textureParams.Length; ++i)
            {
                Texture2D currentTexture = sourceTextures[i];

                if (sourceTextures[i] == null)
                {
                    gen.textureParams[i].dice = false;
                    gen.textureParams[i].anchor = tk2dSpriteCollectionDefinition.Anchor.MiddleCenter;
                    gen.textureParams[i].name = "";
                    gen.textureParams[i].extractRegion = false;
                    gen.textureParams[i].fromSpriteSheet = false;

                    currentTexture = blankTexture;
                }
                else
                {
                    if (gen.textureParams[i].name == null || gen.textureParams[i].name == "")
                    {
                        if (gen.textureParams[i].texture != currentTexture && !gen.textureParams[i].fromSpriteSheet)
                        {
                            gen.textureParams[i].name = currentTexture.name;
                        }
                    }
                }

                if (gen.textureParams[i].dice)
                {
                    // prepare to dice this up
                    Texture2D srcTex = currentTexture;
                    int diceUnitX = (int)(gen.textureParams[i].diceUnitX * gen.globalTextureRescale);
                    int diceUnitY = (int)(gen.textureParams[i].diceUnitY * gen.globalTextureRescale);
                    if (diceUnitX <= 0) diceUnitX = srcTex.width; // something sensible, please
                    if (diceUnitY <= 0) diceUnitY = srcTex.height; // make square if not set

                    for (int sx = 0; sx < srcTex.width; sx += diceUnitX)
                    {
                        for (int sy = 0; sy < srcTex.height; sy += diceUnitY)
                        {
                            int tw = Mathf.Min(diceUnitX, srcTex.width - sx);
                            int th = Mathf.Min(diceUnitY, srcTex.height - sy);

                            if (!gen.textureParams[i].disableTrimming && !gen.disableTrimming)
                            {
                                if (gen.textureParams[i].diceFilter ==
                                    tk2dSpriteCollectionDefinition.DiceFilter.SolidOnly &&
                                    !TextureRectFullySolid(srcTex, sx, sy, tw, th))
                                {
                                    continue;
                                }

                                if (gen.textureParams[i].diceFilter ==
                                    tk2dSpriteCollectionDefinition.DiceFilter.TransparentOnly &&
                                    TextureRectFullySolid(srcTex, sx, sy, tw, th))
                                {
                                    continue;
                                }
                            }

                            SpriteLut diceLut = new SpriteLut();
                            diceLut.source = i;
                            diceLut.isSplit = true;
                            diceLut.sourceTex = srcTex;
                            diceLut.isDuplicate =
                                false; // duplicate diced textures can be chopped up differently, so don't detect dupes here

                            Texture2D dest = ProcessTexture(gen, gen.textureParams[i].additive,
                                tk2dSpriteCollectionDefinition.Pad.Extend, gen.textureParams[i].disableTrimming, false,
                                true, srcTex, sx, sy, tw, th, ref diceLut, GetPadAmount(gen, i));
                            if (dest)
                            {
                                diceLut.atlasIndex = numTexturesToAtlas++;
                                spriteLuts.Add(diceLut);
                            }
                        }
                    }
                }
                else
                {
                    SpriteLut lut = new SpriteLut();
                    lut.sourceTex = currentTexture;
                    lut.source = i;

                    lut.isSplit = false;
                    lut.isDuplicate = false;
                    for (int j = 0; j < spriteLuts.Count; ++j)
                    {
                        if (spriteLuts[j].sourceTex == lut.sourceTex)
                        {
                            lut.isDuplicate = true;
                            lut.atlasIndex = spriteLuts[j].atlasIndex;
                            lut.tex = spriteLuts[j].tex; // get old processed tex

                            lut.rx = spriteLuts[j].rx;
                            lut.ry = spriteLuts[j].ry;
                            lut.rw = spriteLuts[j].rw;
                            lut.rh = spriteLuts[j].rh;

                            break;
                        }
                    }

                    if (!lut.isDuplicate)
                    {
                        lut.atlasIndex = numTexturesToAtlas++;
                        Texture2D dest = ProcessTexture(gen, gen.textureParams[i].additive, gen.textureParams[i].pad,
                            gen.textureParams[i].disableTrimming, false, false, currentTexture, 0, 0,
                            currentTexture.width, currentTexture.height, ref lut, GetPadAmount(gen, i));
                        if (dest == null)
                        {
                            // fall back to a tiny blank texture
                            lut.tex = new Texture2D(1, 1);
                            lut.tex.hideFlags = HideFlags.DontSave;
                            lut.tex.SetPixel(0, 0, new Color(0, 0, 0, 0));
                            PadTexture(lut.tex, GetPadAmount(gen, i), gen.textureParams[i].pad);
                            lut.tex.Apply();

                            lut.rx = currentTexture.width / 2;
                            lut.ry = currentTexture.height / 2;
                            lut.rw = 1;
                            lut.rh = 1;
                        }
                    }

                    spriteLuts.Add(lut);
                }
            }
            
            if (gen.removeDuplicates)
            {
                // Set texture hashes on SpriteLuts
                foreach (var lut in spriteLuts)
                {
                    SetSpriteLutHash(lut);
                }

                // Find more duplicates based on the hash
                for (int i = 0; i < spriteLuts.Count; ++i)
                {
                    for (int j = i + 1; j < spriteLuts.Count; ++j)
                    {
                        if (!spriteLuts[j].isDuplicate)
                        {
                            if (spriteLuts[i].hash == spriteLuts[j].hash)
                            {
                                spriteLuts[j].isDuplicate = true;
                                Object.DestroyImmediate(spriteLuts[j].tex);

                                foreach (var lut in spriteLuts)
                                {
                                    if (lut.atlasIndex > spriteLuts[j].atlasIndex)
                                        --lut.atlasIndex;
                                }

                                --numTexturesToAtlas;

                                spriteLuts[j].atlasIndex = spriteLuts[i].atlasIndex;
                            }
                        }
                    }
                }
            }
            
            // Create texture
            Texture2D[] textureList = new Texture2D[numTexturesToAtlas];
            int titer = 0;
            for (int i = 0; i < spriteLuts.Count; ++i)
            {
                SpriteLut _lut = spriteLuts[i];
                if (!_lut.isDuplicate)
                {
                    textureList[titer++] = _lut.tex;
                }
            }
            
            // Build atlas
            bool forceAtlasSize = gen.forceTextureSize;
            int atlasWidth = forceAtlasSize ? gen.forcedTextureWidth : gen.maxTextureSize;
            int atlasHeight = forceAtlasSize ? gen.forcedTextureHeight : gen.maxTextureSize;
            bool forceSquareAtlas = forceAtlasSize ? false : gen.forceSquareAtlas;
            bool allowFindingOptimalSize = !forceAtlasSize;
            bool allowRotation = !gen.disableRotation;
            Builder atlasBuilder = new Builder(atlasWidth, atlasHeight, gen.allowMultipleAtlases ? 64 : 1,
                allowFindingOptimalSize, forceSquareAtlas, allowRotation);
            if (textureList.Length > 0)
            {
                foreach (Texture2D currTexture in textureList)
                {
                    atlasBuilder.AddRect(currTexture.width, currTexture.height);
                }
            
                if (atlasBuilder.Build() != 0)
                {
                    return false;
                }
            }
            
            // Fill atlas textures
            List<Material> oldAtlasMaterials = new List<Material>(gen.atlasMaterials);
            List<Texture2D> oldAtlasTextures = new List<Texture2D>(gen.atlasTextures);
            List<TextAsset> oldAtlasTextureFiles = new List<TextAsset>(gen.atlasTextureFiles);
            
            Data[] atlasData = atlasBuilder.GetAtlasData();//Emoter.Data.ToArray();
            if (gen.atlasFormat == tk2dSpriteCollection.AtlasFormat.UnityTexture)
            {
                System.Array.Resize(ref gen.atlasTextures, atlasData.Length);
                System.Array.Resize(ref gen.atlasTextureFiles, 0);
            }
            else
            {
                System.Array.Resize(ref gen.atlasTextures, 0);
                System.Array.Resize(ref gen.atlasTextureFiles, atlasData.Length);
            }
            
            System.Array.Resize(ref gen.atlasMaterials, atlasData.Length);
            if (atlasData.Length > 1)
            {
                // wipe out alt materials when atlas spanning is on
                gen.altMaterials = new Material[0];
            }
            
            for (int atlasIndex = 0; atlasIndex < atlasData.Length; ++atlasIndex)
            {
                Texture2D tex = new Texture2D(atlasData[atlasIndex].width, atlasData[atlasIndex].height,
                    TextureFormat.ARGB32, false);
                tex.hideFlags = HideFlags.DontSave;
                gen.atlasWastage = (1.0f - atlasData[0].occupancy) * 100.0f;
                gen.atlasWidth = atlasData[0].width;
                gen.atlasHeight = atlasData[0].height;
                
                for (int yy = 0; yy < tex.height; ++yy)
                {
                    for (int xx = 0; xx < tex.width; ++xx)
                    {
                        tex.SetPixel(xx, yy, Color.clear);
                    }
                }

                for (int i = 0; i < atlasData[atlasIndex].entries.Length; ++i)
                {
                    var entry = atlasData[atlasIndex].entries[i];
                    Texture2D source = textureList[entry.index];

                    if (!entry.flipped)
                    {
                        for (int y = 0; y < source.height; ++y)
                        {
                            for (int x = 0; x < source.width; ++x)
                            {
                                tex.SetPixel(entry.x + x, entry.y + y, source.GetPixel(x, y));
                            }
                        }
                    }
                    else
                    {
                        for (int y = 0; y < source.height; ++y)
                        {
                            for (int x = 0; x < source.width; ++x)
                            {
                                tex.SetPixel(entry.x + y, entry.y + x, source.GetPixel(x, y));
                            }
                        }
                    }
                }

                tex.Apply();

                //Object.DestroyImmediate(tex);
                
                // Get a reference to the texture asset
                if (gen.atlasFormat == tk2dSpriteCollection.AtlasFormat.UnityTexture)
                {
                    gen.atlasTextures[atlasIndex] = tex;
                }
                else
                {
                    tex = null;
                }
                
                // Create material if necessary
                if (gen.atlasMaterials[atlasIndex] == null)
                {
                    Material mat;
                    if (gen.premultipliedAlpha)
                        mat = new Material(Shader.Find("tk2d/PremulVertexColor"));
                    else
                        mat = new Material(Shader.Find("Sprites/Default-ColorFlash"));

                    mat.mainTexture = tex;
                }
                else
                {
                    gen.atlasMaterials[atlasIndex].mainTexture = tex;
                    tk2dUtil.SetDirty(gen.atlasMaterials[atlasIndex]);
                }
                
                // gen.altMaterials must either have length 0, or contain at least the material used in the game
                if (!gen.allowMultipleAtlases && (gen.altMaterials == null || gen.altMaterials.Length == 0))
                    gen.altMaterials = new Material[1] { gen.atlasMaterials[0] };
            }
            
            tk2dSpriteCollectionData coll = gen.spriteCollection;
            coll.textures = new Texture[gen.atlasTextures.Length];
            for (int i = 0; i < gen.atlasTextures.Length; ++i)
            {
                coll.textures[i] = gen.atlasTextures[i];
            }
            
            coll.pngTextures = new TextAsset[gen.atlasTextureFiles.Length];
            for (int i = 0; i < gen.atlasTextureFiles.Length; ++i)
            {
                coll.pngTextures[i] = gen.atlasTextureFiles[i];
            }
            
            if (!gen.allowMultipleAtlases && gen.altMaterials.Length > 1)
            {
                coll.materials = new Material[gen.altMaterials.Length];
                coll.materialPngTextureId = new int[gen.altMaterials.Length];
                for (int i = 0; i < gen.altMaterials.Length; ++i)
                {
                    coll.materials[i] = gen.altMaterials[i];
                    coll.materialPngTextureId[i] = 0;
                }
            }
            else
            {
                coll.materials = new Material[gen.atlasMaterials.Length];
                coll.materialPngTextureId = new int[gen.atlasMaterials.Length];
                for (int i = 0; i < gen.atlasMaterials.Length; ++i)
                {
                    coll.materials[i] = gen.atlasMaterials[i];
                    coll.materialPngTextureId[i] = i;
                }
            }
            
            // Wipe out legacy data
            coll.material = null;
            coll.ClearDictionary();

            coll.premultipliedAlpha = gen.premultipliedAlpha;
            coll.spriteDefinitions = new tk2dSpriteDefinition[gen.textureParams.Length];
            coll.version = tk2dSpriteCollectionData.CURRENT_VERSION;
            coll.materialIdsValid = true;
            coll.spriteCollectionName = ""; //gen.name;

            int buildKey = Random.Range(0, int.MaxValue);
            while (buildKey == coll.buildKey)
            {
                buildKey = Random.Range(0, int.MaxValue);
            }

            coll.buildKey = buildKey; // a random build number so we can identify changed collections quickly
            
            for (int i = 0; i < coll.spriteDefinitions.Length; ++i)
            {
                coll.spriteDefinitions[i] = new tk2dSpriteDefinition();
                if (gen.textureParams[i].texture)
                {

                }
                else
                {
                    coll.spriteDefinitions[i].sourceTextureGUID = "";
                }

                coll.spriteDefinitions[i].extractRegion = gen.textureParams[i].extractRegion;
                coll.spriteDefinitions[i].regionX = gen.textureParams[i].regionX;
                coll.spriteDefinitions[i].regionY = gen.textureParams[i].regionY;
                coll.spriteDefinitions[i].regionW = gen.textureParams[i].regionW;
                coll.spriteDefinitions[i].regionH = gen.textureParams[i].regionH;
            }
            
            coll.allowMultipleAtlases = gen.allowMultipleAtlases;
            //coll.dataGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(coll));

            float scale = 1.0f;
            coll.invOrthoSize = 1.0f / gen.sizeDef.OrthoSize;
            coll.halfTargetHeight = 0.5f * gen.sizeDef.TargetHeight;
            scale = (2.0f * gen.sizeDef.OrthoSize / gen.sizeDef.TargetHeight) * gen.globalScale /
                    gen.globalTextureRescale;
            
            // Build textures
            UpdateVertexCache(gen, scale, atlasData, coll, spriteLuts);

            // Free tmp textures
            foreach (var sprite in spriteLuts)
            {
                if (!sprite.isDuplicate)
                {
                    Object.DestroyImmediate(sprite.tex);
                }
            }

            foreach (var tex in allocatedTextures)
            {
                Object.DestroyImmediate(tex);
            }
            
            // save changes
            gen.spriteCollection.loadable = gen.loadable;
            gen.spriteCollection.assetName = gen.assetName;
            gen.spriteCollection.managedSpriteCollection = gen.managedSpriteCollection;
            gen.spriteCollection.needMaterialInstance = (gen.managedSpriteCollection ||
                                                         gen.atlasFormat != tk2dSpriteCollection.AtlasFormat
                                                             .UnityTexture);
            gen.spriteCollection.textureFilterMode = gen.filterMode;
            gen.spriteCollection.textureMipMaps = gen.mipmapEnabled;

            tk2dUtil.SetDirty(gen.spriteCollection);
            tk2dUtil.SetDirty(gen);

            sourceTextures = null; // need to clear, its static
            currentBuild = null;

            // refresh existing
            gen.spriteCollection.ResetPlatformData();
            RefreshExistingAssets(gen.spriteCollection);

            // post build callback
            if (OnPostBuildSpriteCollection != null)
            {
                OnPostBuildSpriteCollection(gen);
            }

            return true;
        }

        // Hook into this to be notified when a sprite collection is built
        public static event System.Action<tk2dSpriteCollection> OnPostBuildSpriteCollection = null;

        // pass null to rebuild everything
        static void RefreshExistingAssets(tk2dSpriteCollectionData spriteCollectionData)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    System.Type[] types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.GetInterface("tk2dRuntime.ISpriteCollectionForceBuild") != null)
                        {
                            Object[] objects = Resources.FindObjectsOfTypeAll(type);
                            foreach (var o in objects)
                            {
                                tk2dRuntime.ISpriteCollectionForceBuild isc =
                                    o as tk2dRuntime.ISpriteCollectionForceBuild;
                                if (spriteCollectionData == null || isc.UsesSpriteCollection(spriteCollectionData))
                                    isc.ForceBuild();
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        static void UpdateVertexCache(tk2dSpriteCollection gen, float scale, Data[] packers,
            tk2dSpriteCollectionData coll, List<SpriteLut> spriteLuts)
        {
            for (int i = 0; i < sourceTextures.Length; ++i)
            {
                SpriteLut _lut = null;
                for (int j = 0; j < spriteLuts.Count; ++j)
                {
                    if (spriteLuts[j].source == i)
                    {
                        _lut = spriteLuts[j];
                        break;
                    }
                }

                int padAmount = GetPadAmount(gen, i);

                tk2dSpriteCollectionDefinition thisTexParam = gen.textureParams[i];
                Data packer = packers[0];
                Entry atlasEntry = null;
                int atlasIndex = 0;
                if (_lut != null)
                {
                    foreach (var p in packers)
                    {
                        if ((atlasEntry = p.FindEntryWithIndex(_lut.atlasIndex)) != null)
                        {
                            packer = p;
                            break;
                        }

                        ++atlasIndex;
                    }
                }

                float fwidth = packer.width;
                float fheight = packer.height;

                int tx = 0, ty = 0, tw = 0, th = 0;
                if (atlasEntry != null)
                {
                    tx = atlasEntry.x + padAmount;
                    ty = atlasEntry.y + padAmount;
                    tw = atlasEntry.w - padAmount * 2;
                    th = atlasEntry.h - padAmount * 2;
                }

                int sd_y = packer.height - ty - th;

                float uvOffsetX = 0.001f / fwidth;
                float uvOffsetY = 0.001f / fheight;

                Vector2 v0 = new Vector2(tx / fwidth + uvOffsetX, 1.0f - (sd_y + th) / fheight + uvOffsetY);
                Vector2 v1 = new Vector2((tx + tw) / fwidth - uvOffsetX, 1.0f - sd_y / fheight - uvOffsetY);

                Mesh mesh = null;
                Transform meshTransform = null;
                GameObject instantiated = null;

                Vector3 colliderOrigin = new Vector3();

                if (thisTexParam.overrideMesh)
                {
                    // Disabled
                    instantiated = GameObject.Instantiate(thisTexParam.overrideMesh) as GameObject;
                    MeshFilter meshFilter = instantiated.GetComponentInChildren<MeshFilter>();
                    if (meshFilter == null)
                    {
                        XPressions.Instance.LogError("Unable to find mesh");
                        GameObject.DestroyImmediate(instantiated);
                    }
                    else
                    {
                        mesh = meshFilter.sharedMesh;
                        meshTransform = meshFilter.gameObject.transform;
                    }
                }

                Vector3 untrimmedPos0 = Vector3.zero, untrimmedPos1 = Vector3.one;

                if (mesh)
                {
                    coll.spriteDefinitions[i].positions = new Vector3[mesh.vertices.Length];
                    coll.spriteDefinitions[i].uvs = new Vector2[mesh.vertices.Length];
                    for (int j = 0; j < mesh.vertices.Length; ++j)
                    {
                        coll.spriteDefinitions[i].positions[j] = meshTransform.TransformPoint(mesh.vertices[j]);
                        coll.spriteDefinitions[i].uvs[j] = new Vector2(
                            v0.x + (v1.x - v0.x) * mesh.uv[j].x,
                            v0.y + (v1.y - v0.y) * mesh.uv[j].y
                        );
                    }

                    coll.spriteDefinitions[i].indices = new int[mesh.triangles.Length];
                    for (int j = 0; j < mesh.triangles.Length; ++j)
                    {
                        coll.spriteDefinitions[i].indices[j] = mesh.triangles[j];
                    }

                    GameObject.DestroyImmediate(instantiated);
                }
                else
                {
                    Texture2D thisTextureRef = sourceTextures[i];

                    int texHeightI = thisTextureRef ? thisTextureRef.height : 2;
                    int texWidthI = thisTextureRef ? thisTextureRef.width : 2;
                    float texHeight = texHeightI;
                    float texWidth = texWidthI;

                    float h = thisTextureRef ? thisTextureRef.height : 64;
                    float w = thisTextureRef ? thisTextureRef.width : 64;
                    h *= thisTexParam.scale.y;
                    w *= thisTexParam.scale.x;

                    float scaleX = w * scale;
                    float scaleY = h * scale;

                    float anchorX = 0, anchorY = 0;

                    // anchor coordinate system is (0, 0) = top left, to keep it the same as photoshop, etc.
                    switch (thisTexParam.anchor)
                    {
                        case tk2dSpriteCollectionDefinition.Anchor.LowerLeft:
                            anchorX = 0;
                            anchorY = texHeightI;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.LowerCenter:
                            anchorX = texWidthI / 2;
                            anchorY = texHeightI;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.LowerRight:
                            anchorX = texWidthI;
                            anchorY = texHeightI;
                            break;

                        case tk2dSpriteCollectionDefinition.Anchor.MiddleLeft:
                            anchorX = 0;
                            anchorY = texHeightI / 2;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.MiddleCenter:
                            anchorX = texWidthI / 2;
                            anchorY = texHeightI / 2;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.MiddleRight:
                            anchorX = texWidthI;
                            anchorY = texHeightI / 2;
                            break;

                        case tk2dSpriteCollectionDefinition.Anchor.UpperLeft:
                            anchorX = 0;
                            anchorY = 0;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.UpperCenter:
                            anchorX = texWidthI / 2;
                            anchorY = 0;
                            break;
                        case tk2dSpriteCollectionDefinition.Anchor.UpperRight:
                            anchorX = texWidthI;
                            anchorY = 0;
                            break;

                        case tk2dSpriteCollectionDefinition.Anchor.Custom:
                            anchorX = thisTexParam.anchorX * gen.globalTextureRescale;
                            anchorY = thisTexParam.anchorY * gen.globalTextureRescale;
                            break;
                    }

                    Vector3 pos0 = new Vector3(-anchorX * thisTexParam.scale.x * scale, 0,
                        -(h - anchorY * thisTexParam.scale.y) * scale);

                    colliderOrigin = new Vector3(pos0.x, pos0.z, 0.0f);
                    Vector3 pos1 = pos0 + new Vector3(scaleX, 0, scaleY);

                    untrimmedPos0 = new Vector3(pos0.x, pos0.z);
                    untrimmedPos1 = new Vector3(pos1.x, pos1.z);

                    List<Vector3> positions = new List<Vector3>();
                    List<Vector2> uvs = new List<Vector2>();
                    List<int> materialIndex = new List<int>();

                    // Keep track of unique materials per sprite
                    HashSet<int> uniqueMaterialIndices = new HashSet<int>();
                    uniqueMaterialIndices.Add(atlasIndex);

                    // Keep track of material start indices
                    List<int> materialStartIndices = new List<int>();
                    materialStartIndices.Add(atlasIndex);
                    materialStartIndices.Add(0);
                    materialStartIndices.Add(6);

                    // build mesh
                    if (_lut != null && _lut.isSplit)
                    {
                        coll.spriteDefinitions[i].flipped =
                            tk2dSpriteDefinition.FlipMode.None; // each split could be rotated, but not consistently

                        for (int j = 0; j < spriteLuts.Count; ++j)
                        {
                            if (spriteLuts[j].source == i)
                            {
                                _lut = spriteLuts[j];

                                int thisAtlasIndex = 0;
                                foreach (var p in packers)
                                {
                                    if ((atlasEntry = p.FindEntryWithIndex(_lut.atlasIndex)) != null)
                                    {
                                        packer = p;
                                        break;
                                    }

                                    ++thisAtlasIndex;
                                }

                                if (thisAtlasIndex != atlasIndex)
                                {
                                    // This used to be a serious problem, dicing is now partly supported when multi atlas output is selected
                                    atlasIndex = thisAtlasIndex;
                                    uniqueMaterialIndices.Add(atlasIndex);
                                }

                                fwidth = packer.width;
                                fheight = packer.height;

                                tx = atlasEntry.x + padAmount;
                                ty = atlasEntry.y + padAmount;
                                tw = atlasEntry.w - padAmount * 2;
                                th = atlasEntry.h - padAmount * 2;

                                sd_y = packer.height - ty - th;
                                v0 = new Vector2(tx / fwidth + uvOffsetX, 1.0f - (sd_y + th) / fheight + uvOffsetY);
                                v1 = new Vector2((tx + tw) / fwidth - uvOffsetX, 1.0f - sd_y / fheight - uvOffsetY);

                                float x0 = _lut.rx / texWidth;
                                float y0 = _lut.ry / texHeight;
                                float x1 = (_lut.rx + _lut.rw) / texWidth;
                                float y1 = (_lut.ry + _lut.rh) / texHeight;

                                Vector3 dpos0 = new Vector3(Mathf.Lerp(pos0.x, pos1.x, x0), 0.0f,
                                    Mathf.Lerp(pos0.z, pos1.z, y0));
                                Vector3 dpos1 = new Vector3(Mathf.Lerp(pos0.x, pos1.x, x1), 0.0f,
                                    Mathf.Lerp(pos0.z, pos1.z, y1));

                                positions.Add(new Vector3(dpos0.x, dpos0.z, 0));
                                positions.Add(new Vector3(dpos1.x, dpos0.z, 0));
                                positions.Add(new Vector3(dpos0.x, dpos1.z, 0));
                                positions.Add(new Vector3(dpos1.x, dpos1.z, 0));

                                materialIndex.Add(atlasIndex);
                                materialIndex.Add(atlasIndex);
                                materialIndex.Add(atlasIndex);
                                materialIndex.Add(atlasIndex);

                                if (atlasEntry.flipped)
                                {
                                    uvs.Add(new Vector2(v0.x, v0.y));
                                    uvs.Add(new Vector2(v0.x, v1.y));
                                    uvs.Add(new Vector2(v1.x, v0.y));
                                    uvs.Add(new Vector2(v1.x, v1.y));
                                }
                                else
                                {
                                    uvs.Add(new Vector2(v0.x, v0.y));
                                    uvs.Add(new Vector2(v1.x, v0.y));
                                    uvs.Add(new Vector2(v0.x, v1.y));
                                    uvs.Add(new Vector2(v1.x, v1.y));
                                }
                            }
                        }

                        // If this sprite consists of multiple materials...
                        if (uniqueMaterialIndices.Count > 1)
                        {
                            // Sort by materialIndex
                            List<Vector3> newPositions = new List<Vector3>(positions.Count);
                            List<Vector2> newUvs = new List<Vector2>(uvs.Count);
                            List<int> newMaterialIndex = new List<int>(materialIndex.Count);
                            int numVertices = positions.Count;

                            materialStartIndices.Clear();

                            // This isn't particularly efficient, iterates over data N times, where N is number of materials
                            // Use case shouldn't suffer too much though. We're trying to retain the original order as much 
                            // as possible here.
                            foreach (int uniqueMaterialIndex in uniqueMaterialIndices)
                            {
                                int numVerticesUsingMaterial = 0;
                                for (int vi = 0; vi < numVertices; ++vi)
                                {
                                    if (materialIndex[vi] == uniqueMaterialIndex)
                                    {
                                        numVerticesUsingMaterial++;
                                    }
                                }

                                materialStartIndices.Add(uniqueMaterialIndex);
                                materialStartIndices.Add((newPositions.Count / 4) * 6);
                                materialStartIndices.Add((numVerticesUsingMaterial / 4) * 6);

                                for (int vi = 0; vi < numVertices; ++vi)
                                {
                                    if (materialIndex[vi] == uniqueMaterialIndex)
                                    {
                                        newPositions.Add(positions[vi]);
                                        newUvs.Add(uvs[vi]);
                                        newMaterialIndex.Add(materialIndex[vi]);
                                    }
                                }
                            }

                            // Override previous copy
                            positions = newPositions;
                            uvs = newUvs;
                            materialIndex = newMaterialIndex;
                        }
                    }
                    else if (thisTexParam.customSpriteGeometry)
                    {
                        coll.spriteDefinitions[i].flipped = atlasEntry.flipped
                            ? tk2dSpriteDefinition.FlipMode.Tk2d
                            : tk2dSpriteDefinition.FlipMode.None;

                        List<int> indices = new List<int>();
                        foreach (var island in thisTexParam.geometryIslands)
                        {
                            int baseIndex = positions.Count;
                            for (int x = 0; x < island.points.Length; ++x)
                            {
                                var v = island.points[x] * gen.globalTextureRescale;
                                Vector2 origin = new Vector2(pos0.x, pos0.z);
                                positions.Add(
                                    new Vector2(v.x * thisTexParam.scale.x, (texHeight - v.y) * thisTexParam.scale.y) *
                                    scale + new Vector2(origin.x, origin.y));
                                materialIndex.Add(atlasIndex);

                                tx = atlasEntry.x + padAmount;
                                ty = atlasEntry.y + padAmount;
                                tw = atlasEntry.w - padAmount * 2;
                                th = atlasEntry.h - padAmount * 2;

                                //v0 = new Vector2(tx / fwidth + uvOffsetX, 1.0f - (sd_y + th) / fheight + uvOffsetY);
                                //v1 = new Vector2((tx + tw) / fwidth - uvOffsetX, 1.0f - sd_y / fheight - uvOffsetY);

                                Vector2 uv = new Vector2();
                                if (atlasEntry.flipped)
                                {
                                    uv.x = (tx - _lut.ry + texHeight - v.y) / fwidth + uvOffsetX;
                                    uv.y = (ty - _lut.rx + v.x) / fheight + uvOffsetY;
                                }
                                else
                                {
                                    uv.x = (tx - _lut.rx + v.x) / fwidth + uvOffsetX;
                                    uv.y = (ty - _lut.ry + texHeight - v.y) / fheight + uvOffsetY;
                                }

                                uvs.Add(uv);
                            }

                            Triangulator triangulator = new Triangulator(island.points);
                            int[] localIndices = triangulator.Triangulate();
                            //for (int x = localIndices.Length - 1; x >= 0; --x)
                            for (int x = 0; x < localIndices.Length; x += 3)
                            {
                                indices.Add(baseIndex + localIndices[x + 2]);
                                indices.Add(baseIndex + localIndices[x + 1]);
                                indices.Add(baseIndex + localIndices[x + 0]);
                            }
                        }

                        coll.spriteDefinitions[i].indices = indices.ToArray();
                    }
                    else
                    {
                        bool flipped = (atlasEntry != null && atlasEntry.flipped);
                        coll.spriteDefinitions[i].flipped =
                            flipped ? tk2dSpriteDefinition.FlipMode.Tk2d : tk2dSpriteDefinition.FlipMode.None;

                        float x0 = 0, y0 = 0;
                        float x1 = 0, y1 = 0;

                        if (_lut != null)
                        {
                            x0 = _lut.rx / texWidth;
                            y0 = _lut.ry / texHeight;
                            x1 = (_lut.rx + _lut.rw) / texWidth;
                            y1 = (_lut.ry + _lut.rh) / texHeight;
                        }

                        Vector3 dpos0 = new Vector3(Mathf.Lerp(pos0.x, pos1.x, x0), 0.0f,
                            Mathf.Lerp(pos0.z, pos1.z, y0));
                        Vector3 dpos1 = new Vector3(Mathf.Lerp(pos0.x, pos1.x, x1), 0.0f,
                            Mathf.Lerp(pos0.z, pos1.z, y1));

                        positions.Add(new Vector3(dpos0.x, dpos0.z, 0));
                        positions.Add(new Vector3(dpos1.x, dpos0.z, 0));
                        positions.Add(new Vector3(dpos0.x, dpos1.z, 0));
                        positions.Add(new Vector3(dpos1.x, dpos1.z, 0));
                        materialIndex.Add(atlasIndex);
                        materialIndex.Add(atlasIndex);
                        materialIndex.Add(atlasIndex);
                        materialIndex.Add(atlasIndex);

                        if (flipped)
                        {
                            uvs.Add(new Vector2(v0.x, v0.y));
                            uvs.Add(new Vector2(v0.x, v1.y));
                            uvs.Add(new Vector2(v1.x, v0.y));
                            uvs.Add(new Vector2(v1.x, v1.y));
                        }
                        else
                        {
                            uvs.Add(new Vector2(v0.x, v0.y));
                            uvs.Add(new Vector2(v1.x, v0.y));
                            uvs.Add(new Vector2(v0.x, v1.y));
                            uvs.Add(new Vector2(v1.x, v1.y));
                        }

                        if (thisTexParam.doubleSidedSprite)
                        {
                            positions.Add(positions[3]);
                            uvs.Add(uvs[3]);
                            positions.Add(positions[1]);
                            uvs.Add(uvs[1]);
                            positions.Add(positions[2]);
                            uvs.Add(uvs[2]);
                            positions.Add(positions[0]);
                            uvs.Add(uvs[0]);
                            materialIndex.Add(atlasIndex);
                            materialIndex.Add(atlasIndex);
                            materialIndex.Add(atlasIndex);
                            materialIndex.Add(atlasIndex);
                        }
                    }

                    // build sprite definition
                    if (!thisTexParam.customSpriteGeometry)
                    {
                        coll.spriteDefinitions[i].indices = new int[6 * (positions.Count / 4)];
                        for (int j = 0; j < positions.Count / 4; ++j)
                        {
                            coll.spriteDefinitions[i].indices[j * 6 + 0] = j * 4 + 0;
                            coll.spriteDefinitions[i].indices[j * 6 + 1] = j * 4 + 3;
                            coll.spriteDefinitions[i].indices[j * 6 + 2] = j * 4 + 1;
                            coll.spriteDefinitions[i].indices[j * 6 + 3] = j * 4 + 2;
                            coll.spriteDefinitions[i].indices[j * 6 + 4] = j * 4 + 3;
                            coll.spriteDefinitions[i].indices[j * 6 + 5] = j * 4 + 0;
                        }

                        coll.spriteDefinitions[i].complexGeometry = false;
                    }
                    else
                    {
                        coll.spriteDefinitions[i].complexGeometry = true;
                    }

                    //coll.spriteDefinitions[i].materialIndexData = materialStartIndices.ToArray();

                    coll.spriteDefinitions[i].positions = new Vector3[positions.Count];
                    coll.spriteDefinitions[i].uvs = new Vector2[uvs.Count];
                    for (int j = 0; j < positions.Count; ++j)
                    {
                        coll.spriteDefinitions[i].positions[j] = positions[j];
                        coll.spriteDefinitions[i].uvs[j] = uvs[j];
                    }

                    coll.spriteDefinitions[i].normalizedUvs = CalculateNormalizedUvs(uvs);

                    // empty out to a sensible default, which corresponds to what Unity does by default
                    coll.spriteDefinitions[i].normals = new Vector3[0];
                    coll.spriteDefinitions[i].tangents = new Vector4[0];

                    // fill out tangents and normals
                    if (gen.normalGenerationMode != tk2dSpriteCollection.NormalGenerationMode.None)
                    {
                        Mesh tmpMesh = new Mesh();
                        tmpMesh.vertices = coll.spriteDefinitions[i].positions;
                        tmpMesh.uv = coll.spriteDefinitions[i].uvs;
                        tmpMesh.triangles = coll.spriteDefinitions[i].indices;

                        tmpMesh.RecalculateNormals();

                        coll.spriteDefinitions[i].normals = tmpMesh.normals;

                        if (gen.normalGenerationMode == tk2dSpriteCollection.NormalGenerationMode.NormalsAndTangents)
                        {
                            Vector4[] tangents = new Vector4[tmpMesh.normals.Length];
                            for (int t = 0; t < tangents.Length; ++t)
                                tangents[t] = new Vector4(1, 0, 0, 1);
                            coll.spriteDefinitions[i].tangents = tangents;
                        }
                    }
                }

                // fixup in case something went wrong
                if (coll.allowMultipleAtlases)
                {
                    coll.spriteDefinitions[i].material = gen.atlasMaterials[atlasIndex];
                    coll.spriteDefinitions[i].materialId = atlasIndex;
                }
                else
                {
                    // make sure its not overrun, can happen when refs are cleared
                    thisTexParam.materialId = Mathf.Min(thisTexParam.materialId, gen.altMaterials.Length - 1);
                    coll.spriteDefinitions[i].material = gen.altMaterials[thisTexParam.materialId];
                    coll.spriteDefinitions[i].materialId = thisTexParam.materialId;

                    if (coll.spriteDefinitions[i].material == null) // fall back gracefully in case something went wrong
                    {
                        coll.spriteDefinitions[i].material = gen.atlasMaterials[atlasIndex];
                        coll.spriteDefinitions[i].materialId = 0;
                    }
                }

                Vector3 boundsMin = new Vector3(1.0e32f, 1.0e32f, 1.0e32f);
                Vector3 boundsMax = new Vector3(-1.0e32f, -1.0e32f, -1.0e32f);
                foreach (Vector3 v in coll.spriteDefinitions[i].positions)
                {
                    boundsMin = Vector3.Min(boundsMin, v);
                    boundsMax = Vector3.Max(boundsMax, v);
                }

                coll.spriteDefinitions[i].boundsData = new Vector3[2];
                coll.spriteDefinitions[i].boundsData[0] = (boundsMax + boundsMin) / 2.0f;
                coll.spriteDefinitions[i].boundsData[1] = (boundsMax - boundsMin);

                // this is the dimension of exactly one pixel, scaled to match sprite dimensions and scale
                coll.spriteDefinitions[i].texelSize = new Vector3(
                    scale * thisTexParam.scale.x / gen.globalScale * gen.globalTextureRescale,
                    scale * thisTexParam.scale.y / gen.globalScale * gen.globalTextureRescale, 0.0f);

                coll.spriteDefinitions[i].untrimmedBoundsData = new Vector3[2];
                if (mesh)
                {
                    // custom meshes aren't trimmed, the untrimmed bounds are exactly the same as the regular ones
                    coll.spriteDefinitions[i].untrimmedBoundsData[0] = coll.spriteDefinitions[i].boundsData[0];
                    coll.spriteDefinitions[i].untrimmedBoundsData[1] = coll.spriteDefinitions[i].boundsData[1];
                }
                else
                {
                    boundsMin = Vector3.Min(untrimmedPos0, untrimmedPos1);
                    boundsMax = Vector3.Max(untrimmedPos0, untrimmedPos1);
                    coll.spriteDefinitions[i].untrimmedBoundsData[0] = (boundsMax + boundsMin) / 2.0f;
                    coll.spriteDefinitions[i].untrimmedBoundsData[1] = (boundsMax - boundsMin);
                }


                coll.spriteDefinitions[i].name = gen.textureParams[i].name;
            }
        }

        static Vector2[] CalculateNormalizedUvs(List<Vector2> uvs)
        {
            Vector2 min = new Vector2(1.0e32f, 1.0e32f);
            Vector2 max = new Vector2(-1.0e32f, -1.0e32f);
            for (int i = 0; i < uvs.Count; ++i)
            {
                min.x = Mathf.Min(min.x, uvs[i].x);
                min.y = Mathf.Min(min.y, uvs[i].y);
                max.x = Mathf.Max(max.x, uvs[i].x);
                max.y = Mathf.Max(max.y, uvs[i].y);
            }

            Vector2[] normalizedUvs = new Vector2[uvs.Count];
            Vector2 deltaUv = max - min;
            for (int i = 0; i < uvs.Count; ++i)
            {
                Vector2 uv = uvs[i];
                normalizedUvs[i].x = Mathf.Clamp01((uv.x - min.x) / deltaUv.x);
                normalizedUvs[i].y = Mathf.Clamp01((uv.y - min.y) / deltaUv.y);
            }

            return normalizedUvs;
        }
    }
}
