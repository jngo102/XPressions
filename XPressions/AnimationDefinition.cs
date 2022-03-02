using System;
using System.Collections.Generic;
using UnityEngine;

namespace XPressions
{
    [Serializable]
    public class AnimationDefinition
    {
        public Vector2[] anchors;
        public Entry[] entries;
        public int fps;
    }
}
