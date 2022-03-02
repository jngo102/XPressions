using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace XPressions
{
    internal class Emoter : MonoBehaviour
    {
        public static List<Data> Data = new();

        private readonly string _emotesDir = Path.GetFullPath(Path.Combine(Application.dataPath, "Managed", "Mods", XPressions.Instance.GetName()));
        private List<tk2dSpriteCollection> _spriteCollections = new();
        private List<tk2dSpriteAnimation> _spriteAnimations = new();
        private List<tk2dSpriteDefinition> _spriteDefinitions = new();
        private List<Texture2D> _sourceAtlases = new();
        private List<Texture2D> _sourceTextures = new();
        private List<Material> _materials = new();

        private MeshFilter _meshFilter;
        private tk2dSpriteAnimator _animator;
        private tk2dSprite _sprite;

        private bool _emoting;

        private void Awake()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _animator ??= GetComponent<tk2dSpriteAnimator>();
            _sprite ??= GetComponent<tk2dSprite>();
        }

        private void Start()
        {
            LoadEmotes();
            AddEmotes();

            _sprite.ForceBuild();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.V) && !_emoting && !HeroController.instance.controlReqlinquished && HeroController.instance.CheckTouchingGround())
            {
                StartCoroutine(Emote());
            }
        }

        private IEnumerator Emote()
        {
            _emoting = true;
            HeroController.instance.StopAnimationControl();
            HeroController.instance.RelinquishControl();
            yield return new WaitForSeconds(_animator.PlayAnimGetTime(_animator.Library.clips.Last().name));
            HeroController.instance.StartAnimationControl();
            HeroController.instance.RegainControl();
            _emoting = false;
        }

        private void LoadEmotes()
        {
            foreach (string emoteDir in Directory.GetDirectories(_emotesDir))
            {
                string emoteName = Path.GetFileName(emoteDir);
                string atlasFile = Directory.GetFiles(emoteDir).First(file => file.EndsWith(".png"));
                byte[] atlasBytes = File.ReadAllBytes(atlasFile);
                var atlasTexture = new Texture2D(2, 2)
                {
                    name = "atlas0"
                };
                atlasTexture.LoadImage(atlasBytes);
                _sourceAtlases.Add(atlasTexture);
                
                string jsonFile = Directory.GetFiles(emoteDir).First(file => file.EndsWith(".json"));
                string json = File.ReadAllText(jsonFile);
                var animationDefinition = JsonConvert.DeserializeObject<AnimationDefinition>(json);
                var atlasData = new Data
                {
                    entries = animationDefinition.entries,
                    height = atlasTexture.height,
                    width = atlasTexture.width,
                };
                Data.Add(atlasData);

                for (int i = 0; i < atlasData.entries.Length; i++)
                {
                    Texture2D subTexture = atlasTexture.SubTexture(atlasData.entries[i]);
                    subTexture.name = $"{emoteName}_{i:D4}";
                    
                    _sourceTextures.Add(subTexture);
                }
                
                List<tk2dSpriteCollectionDefinition> textureParams = new(atlasData.entries.Length);
                for (int i = 0; i < atlasData.entries.Length; i++)
                {
                    var spriteCollectionDefinition = new tk2dSpriteCollectionDefinition
                    {
                        anchor = tk2dSpriteCollectionDefinition.Anchor.MiddleCenter,
                        anchorX = animationDefinition.anchors[i].x,
                        anchorY = animationDefinition.anchors[i].y,
                        extractRegion = true,
                        name = $"{emoteName}_{i:D4}",
                        pad = tk2dSpriteCollectionDefinition.Pad.Default,
                        regionX = atlasData.entries[i].x,
                        regionY = atlasData.entries[i].y,
                        regionW = atlasData.entries[i].w,
                        regionH = atlasData.entries[i].h,
                        source = tk2dSpriteCollectionDefinition.Source.SpriteSheet,
                        texture = atlasTexture,
                    };

                    textureParams.Add(spriteCollectionDefinition);
                }
                
                var atlasMaterial = new Material(Shader.Find("Sprites/Default-ColorFlash"))
                {
                    mainTexture = atlasTexture,
                    name = atlasTexture.name + " material",
                };
                _materials.Add(atlasMaterial);
                
                var cln = new tk2dSpriteCollection
                {
                    atlasMaterials = new[] { atlasMaterial },
                    atlasTextures = new[] { atlasTexture },
                    disableRotation = false,
                    maxTextureSize = 2048,
                    //name = emoteName + " Cln",
                    removeDuplicates = true,
                    sizeDef = new tk2dSpriteCollectionSize
                    {
                        height = 64,
                        orthoSize = 0.5f,
                        type = tk2dSpriteCollectionSize.Type.Explicit,
                    },
                    textureParams = textureParams.ToArray(),
                };
                
                Mesh mesh = _meshFilter.sharedMesh;
                for (int i = 0; i < atlasData.entries.Length; i++)
                {
                    Entry entry = atlasData.entries[i];

                    var tk2dSpriteDef = new tk2dSpriteDefinition
                    {
                        boundsData = new Vector3[2],
                        colliderIndicesBack = new int[] { },
                        colliderIndicesFwd = new int[] { },
                        colliderVertices = new Vector3[] { },
                        flipped = entry.flipped ? tk2dSpriteDefinition.FlipMode.Tk2d : tk2dSpriteDefinition.FlipMode.None,
                        indices = new int[mesh.triangles.Length],
                        material = atlasMaterial,
                        materialInst = atlasMaterial,
                        normalizedUvs = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) },
                        normals = new Vector3[] { },
                        positions = new Vector3[mesh.vertices.Length],
                        regionX = entry.x,
                        regionY = entry.y,
                        regionW = entry.w,
                        regionH = entry.h,
                        tangents = new Vector4[] { },
                        untrimmedBoundsData = new Vector3[2],
                        uvs = new Vector2[mesh.vertices.Length],
                    };

                    _spriteDefinitions.Add(tk2dSpriteDef);
                }
                
                var dataObj = new GameObject(emoteName + " Cln");
                var data = dataObj.AddComponent<tk2dSpriteCollectionData>();
                data.allowMultipleAtlases = false;
                data.buildKey = Random.Range(0, int.MaxValue);
                data.halfTargetHeight = 32;
                data.invOrthoSize = 2;
                data.materials = new[] { atlasMaterial };
                data.materialInsts = new[] { atlasMaterial };
                data.materialPngTextureId = new[] { 0 };
                data.spriteCollectionName = emoteName;
                data.spriteCollectionPlatformGUIDs = new string[] { };
                data.spriteCollectionPlatforms = new string[] { };
                data.textures = new Texture[] { atlasTexture };
                dataObj.hideFlags = HideFlags.HideAndDontSave;

                cln.spriteCollection = data;
                
                tk2dSpriteCollectionBuilder.Rebuild(cln);
                _spriteCollections.Add(cln);

                List<tk2dSpriteAnimationFrame> frames = new();
                
                for (int index = 0; index < _spriteDefinitions.Count; index++)
                {
                    var frame = new tk2dSpriteAnimationFrame
                    {
                        spriteCollection = data,
                        spriteId = index,
                    };
                    
                    frames.Add(frame);
                }

                var clip = new tk2dSpriteAnimationClip
                {
                    fps = animationDefinition.fps,
                    frames = frames.ToArray(),
                    loopStart = 0,
                    name = emoteName,
                    wrapMode = tk2dSpriteAnimationClip.WrapMode.Once,
                };
                
                var animObj = new GameObject(emoteName + " Anim");
                var animation = animObj.AddComponent<tk2dSpriteAnimation>();
                animation.clips = new[] { clip };
                animObj.hideFlags = HideFlags.HideAndDontSave;
                
                _spriteAnimations.Add(animation);
            }
        }

        private void AddEmotes()    
        {
            //tk2dSpriteCollectionData data = _sprite.Collection;
            //List<tk2dSpriteDefinition> definitions = data.spriteDefinitions.ToList();
            //foreach (tk2dSpriteDefinition def in _spriteDefinitions)
            //{
            //    definitions.Add(def);
            //}

            //data.spriteDefinitions = definitions.ToArray();

            List<tk2dSpriteAnimationClip> clips = _animator.Library.clips.ToList();
            foreach (tk2dSpriteAnimation animation in _spriteAnimations)
            {
                foreach (tk2dSpriteAnimationClip clip in animation.clips)
                {
                    clips.Add(clip);
                }

                _animator.Library.clips = clips.ToArray();
            }
        }
    }
}
