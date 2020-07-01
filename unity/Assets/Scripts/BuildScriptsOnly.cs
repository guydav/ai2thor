#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

public class BuildScriptsOnly : MonoBehaviour
{
    [MenuItem("Build/Build scripts")]
    public static void MyBuild()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        // buildPlayerOptions.scenes = new[] { "Assets/scene.unity" };
        //buildPlayerOptions.locationPathName = " builds/goal-generation-test";
        buildPlayerOptions.target = BuildTarget.WebGL;

        // use these options for the first build
        // buildPlayerOptions.options = BuildOptions.Development;

        // use these options for building scripts
        buildPlayerOptions.options = BuildOptions.BuildScriptsOnly | BuildOptions.Development;

        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }
}

#endif
