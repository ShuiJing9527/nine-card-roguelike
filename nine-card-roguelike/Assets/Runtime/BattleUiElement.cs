using UnityEngine;
namespace Qiling
{
    // Stable binding identity. Decoration and child order may be edited without affecting binding.
    public sealed class BattleUiElement : MonoBehaviour
    {
        [HideInInspector] public string bindingKey;
        public bool useThemeFont = true;
        public bool useThemeSprites = true;
    }
}
