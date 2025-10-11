
using UnityEngine;

public class ForceOrientation : MonoBehaviour
{
    [Tooltip("Sahnede yatay mý olsun?")]
    public bool landscape;

    void OnEnable()
    {
        Screen.autorotateToPortrait = !landscape;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = landscape;
        Screen.autorotateToLandscapeRight = landscape;

        Screen.orientation = landscape ? ScreenOrientation.LandscapeLeft
                                       : ScreenOrientation.Portrait;
    }

    void OnDisable()
    {
        // Sahneden çýkarken geri AutoRotation'a dön
        Screen.autorotateToPortrait = true;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }
}
