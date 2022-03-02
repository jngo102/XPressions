using System;
using Modding;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace XPressions
{
    public class XPressions : Mod, IGlobalSettings<GlobalSettings>
    {
        private static GlobalSettings _globalSettings = new();
        public static GlobalSettings GlobalSettings => _globalSettings;

        private static XPressions _instance;
        public static XPressions Instance => _instance;

        public override string GetVersion() => "1.0.0.0";

        public XPressions() : base("XPressions") { }

        public override void Initialize()
        {
            _instance = this;
            
            On.HeroController.Start += OnHCStart;
        }

        private void OnHCStart(On.HeroController.orig_Start orig, HeroController self)
        {
            orig(self);

            self.gameObject.AddComponent<Emoter>();
        }

        public void OnLoadGlobal(GlobalSettings globalSettings) => _globalSettings = globalSettings;
        public GlobalSettings OnSaveGlobal() => _globalSettings;
    }
}