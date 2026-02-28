using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Minigames/WarForOil/WTETWCEventGroup")]
public class WTETWCEventGroup : ScriptableObject
{
    public string id;
    [TextArea(1, 3)] public string description;
    public List<EventGroupMember> members;
    public int maxTriggerCount = -1; //en fazla kaç event tetiklenebilir (-1 = sınırsız)
}

[System.Serializable]
public class EventGroupMember
{
    public WarForOilEvent warEvent; //grup üyesi event
    public float triggerWeight = 1f; //havuzda seçilme ağırlığı (1 = normal, 0.3 = düşük şans)
}
