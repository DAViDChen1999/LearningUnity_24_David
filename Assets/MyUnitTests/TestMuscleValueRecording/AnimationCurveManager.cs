/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationCurveManager : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class AnimationCurveManager : MonoBehaviour
{

    public AnimationClip clip;
    [Serializable]
    public sealed class ClipInfo
    {
        public int ClipInstanceID;
        public List<CurveInfo> CurveInfos = new List<CurveInfo>();

        // default constructor is sometimes required for (de)serialization
        public ClipInfo() { }

        public ClipInfo(Object clip, List<CurveInfo> curveInfos)
        {
            ClipInstanceID = clip.GetInstanceID();
            CurveInfos = curveInfos;
        }
    }

    [Serializable]
    public sealed class CurveInfo
    {
        public string PathKey;

        public List<KeyFrameInfo> Keys = new List<KeyFrameInfo>();
        public WrapMode PreWrapMode;
        public WrapMode PostWrapMode;

        // default constructor is sometimes required for (de)serialization
        public CurveInfo() { }

        public CurveInfo(string pathKey, AnimationCurve curve)
        {
            PathKey = pathKey;

            foreach (var keyframe in curve.keys)
            {
                Keys.Add(new KeyFrameInfo(keyframe));
            }

            PreWrapMode = curve.preWrapMode;
            PostWrapMode = curve.postWrapMode;
        }
    }

    [Serializable]
    public sealed class KeyFrameInfo
    {
        public float Value;
        public float InTangent;
        public float InWeight;
        public float OutTangent;
        public float OutWeight;
        public float Time;
        public WeightedMode WeightedMode;

        // default constructor is sometimes required for (de)serialization
        public KeyFrameInfo() { }

        public KeyFrameInfo(Keyframe keyframe)
        {
            Value = keyframe.value;
            InTangent = keyframe.inTangent;
            InWeight = keyframe.inWeight;
            OutTangent = keyframe.outTangent;
            OutWeight = keyframe.outWeight;
            Time = keyframe.time;
            WeightedMode = keyframe.weightedMode;
        }
    }

    // I know ... singleton .. but what choices do we have? ;)
    private static AnimationCurveManager _instance;

    public static AnimationCurveManager Instance
    {
        get
        {
            // lazy initialization/instantiation
            if (_instance) return _instance;

            _instance = FindObjectOfType<AnimationCurveManager>();

            if (_instance) return _instance;

            _instance = new GameObject("AnimationCurveManager").AddComponent<AnimationCurveManager>();

            return _instance;
        }
    }

    // Clips to manage e.g. reference these via the Inspector
    public List<AnimationClip> clips = new List<AnimationClip>();

    // every animation curve belongs to a specific clip and 
    // a specific property of a specific component on a specific object
    // for making this easier lets simply use a combined string as key
    private string CurveKey(string pathToObject, Type type, string propertyName)
    {
        return $"{pathToObject}:{type.FullName}:{propertyName}";
    }

    public List<ClipInfo> ClipCurves = new List<ClipInfo>();

    private string filePath = Path.Combine(Application.streamingAssetsPath, "AnimationCurves.dat");

    private void Awake()
    {
        if (_instance && _instance != this)
        {
            Debug.LogWarning("Multiple Instances of AnimationCurveManager! Will ignore this one!", this);
            return;
        }

        _instance = this;

        DontDestroyOnLoad(gameObject);

        // load infos on runtime
        LoadClipCurves();
    }

#if UNITY_EDITOR

    // Call this from the ContextMenu (or later via editor script)
    [ContextMenu("Save Animation Curves")]
    private void SaveAnimationCurves()
    {
        ClipCurves.Clear();

        foreach (var clip in clips)
        {
            var curveInfos = new List<CurveInfo>();
            ClipCurves.Add(new ClipInfo(clip, curveInfos));

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var key = CurveKey(binding.path, binding.type, binding.propertyName);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                curveInfos.Add(new CurveInfo(key, curve));
            }
        }

        // create the StreamingAssets folder if it does not exist
        try
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
            }
        }
        catch (IOException ex)
        {
            Debug.LogError(ex.Message);
        }

        // create a new file e.g. AnimationCurves.dat in the StreamingAssets folder
        var json = JsonConvert.SerializeObject(ClipCurves);
        File.WriteAllText(filePath, json);

        AssetDatabase.Refresh();
    }
#endif

    private void LoadClipCurves()
    {
        if (!File.Exists(filePath))
        {
            Debug.LogErrorFormat(this, "File \"{0}\" not found!", filePath);
            return;
        }

        var fileStream = new FileStream(filePath, FileMode.Open);

        var json = File.ReadAllText(filePath);

        ClipCurves = JsonConvert.DeserializeObject<List<ClipInfo>>(json);
    }

    // now for getting a specific clip's curves
    public AnimationCurve GetCurve(AnimationClip clip, string pathToObject, Type type, string propertyName)
    {
        // either not loaded yet or error -> try again
        if (ClipCurves == null || ClipCurves.Count == 0) LoadClipCurves();

        // still null? -> error
        if (ClipCurves == null || ClipCurves.Count == 0)
        {
            Debug.LogError("Apparantly no clipCurves loaded!");
            return null;
        }

        var clipInfo = ClipCurves.FirstOrDefault(ci => ci.ClipInstanceID == clip.GetInstanceID());

        // does this clip exist in the dictionary?
        if (clipInfo == null)
        {
            Debug.LogErrorFormat(this, "The clip \"{0}\" was not found in clipCurves!", clip.name);
            return null;
        }

        var key = CurveKey(pathToObject, type, propertyName);

        var curveInfo = clipInfo.CurveInfos.FirstOrDefault(c => string.Equals(c.PathKey, key));

        // does the curve key exist for the clip?
        if (curveInfo == null)
        {
            Debug.LogErrorFormat(this, "The key \"{0}\" was not found for clip \"{1}\"", key, clip.name);
            return null;
        }

        var keyframes = new Keyframe[curveInfo.Keys.Count];

        for (var i = 0; i < curveInfo.Keys.Count; i++)
        {
            var keyframe = curveInfo.Keys[i];

            keyframes[i] = new Keyframe(keyframe.Time, keyframe.Value, keyframe.InTangent, keyframe.OutTangent, keyframe.InWeight, keyframe.OutWeight)
            {
                weightedMode = keyframe.WeightedMode
            };
        }

        var curve = new AnimationCurve(keyframes)
        {
            postWrapMode = curveInfo.PostWrapMode,
            preWrapMode = curveInfo.PreWrapMode
        };

        // otherwise finally return the AnimationCurve
        return curve;
    }

    
    // Start is called before the first frame update
    void Start()
    {
        
        SaveAnimationCurves();


        /*
        // you need those of course
        string clipName;
        AnimationCurve originalCurve = AnimationCurveManager.Instance.GetCurve(clip, "some/relative/GameObject", typeof<SomeComponnet>, "somePropertyName");
        // TODO 
        AnimationCurve newCurve = SomeMagic(originalCurve);

        // get the animator reference
        var animator = animatorObject.GetComponent<Animator>();
        // get the runtime Animation controller
        var controller = animator.runtimeAnimatorController;
        // get all clips
        var clips = controller.animationClips;
        // find the specific clip by name
        // alternatively you could also get this as before using a field and
        // reference the according script via the Inspector 
        var someClip = clips.FirstOrDefault(clip => string.Equals(clipName, clip.name));

        // was found?
        if(!someClip)
        {
            Debug.LogWarningFormat(this, "There is no clip called {0}!", clipName);
            return;
        }

        // assign a new curve
        someClip.SetCurve("relative/path/to/some/GameObject", typeof(SomeComponnet), "somePropertyName", newCurve);
        */
    }

    // Update is called once per frame
    void Update()
    {
        
    }

}

