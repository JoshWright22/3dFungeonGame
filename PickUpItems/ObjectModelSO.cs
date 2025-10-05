using UnityEngine;

[CreateAssetMenu(fileName = "Fantasy Object", menuName = "ScriptableObjects/Fantasy Object")]
public class ObjectModelSO : ScriptableObject
{
    [Header("Text")]
    public string nameText;
    public string descriptionText;
    [Header("First Person Right Hand")]
    public Vector3 fprhPos;
    public Quaternion fprhRot;
    public Vector3 fprhSca;
    [Header("First Person Left Hand")]
    public Vector3 fplhPos;
    public Quaternion fplhRot;
    public Vector3 fplhSca;
    [Header("Third Person Right Hand")]
    public Vector3 tprhPos;
    public Quaternion tprhRot;
    public Vector3 tprhSca;
    [Header("Third Person Left Hand")]
    public Vector3 tplhPos;
    public Quaternion tplhRot;
    public Vector3 tplhSca;
    [Header("Data")]
    public Size size;
    public AnimationClass animationClass;
    public GameObject prefab;
}

public enum Size
{
    OneHand,
    TwoHand
}

public enum AnimationClass
{
    Carry,
    Wizard,
    Rogue,
    Bow,
    TwoHandWeapon,
    SwordAndShield
}