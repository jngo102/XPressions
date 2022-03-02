using Modding;
using System.Collections.Generic;
using UnityEngine;

namespace XPressions
{
    public static class Utility
    {
        public static Texture2D SubTexture(this Texture2D sourceTexture, Entry entry)
        {
            var outTexture = new Texture2D(entry.w, entry.h);

            for (int x = 0; x < outTexture.width; x++)
            {
                for (int y = 0; y < outTexture.height; y++)
                {
                    outTexture.SetPixel(x, y, sourceTexture.GetPixel(entry.x + x, entry.y + y));
                }
            }

            if (entry.flipped)
            {
                UndoFlip(ref outTexture);
            }

            outTexture.Apply();

            return outTexture;
        }

        public static void UndoFlip(ref Texture2D texture)
        {
            var unflippedTexture = new Texture2D(texture.height, texture.width);
            for (int x = 0; x < texture.width; x++)
            {
                for (int y = 0; y < texture.height; y++)
                {
                    unflippedTexture.SetPixel(texture.height - y, x, texture.GetPixel(x, y));
                }
            }

            texture = unflippedTexture;
        }
    }
}
