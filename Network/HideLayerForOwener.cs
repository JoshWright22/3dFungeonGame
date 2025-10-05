using UnityEngine;
using Unity.Netcode;

public class HideLayerForOwener : NetworkBehaviour
{

    public bool hideAllButShadows;
    public override void OnNetworkSpawn()
    {

        SetLayerAllChildren(this.transform);

    }

    void SetLayerAllChildren(Transform root)
    {
        var children = root.GetComponentsInChildren<Transform>(includeInactive: true);
        foreach (var child in children)
        {
            //Debug.Log(child.name);
            //child.gameObject.layer = layer;
            if (child.gameObject.GetComponent<SkinnedMeshRenderer>() != null && hideAllButShadows)
            {
                child.gameObject.GetComponent<SkinnedMeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            else if (child.gameObject.GetComponent<SkinnedMeshRenderer>())
            {
                child.gameObject.GetComponent<SkinnedMeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

        }
    }
}