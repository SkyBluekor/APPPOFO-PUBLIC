using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AppPofo.Presentation.Common
{
    // uGUI's OnDeselect appends the current IME composition even after Enter
    // or OnDisable has already ended editing. Ignore that late second commit.
    public sealed class CompositionSafeInputField : InputField
    {
        public override void OnDeselect(BaseEventData eventData)
        {
            if (!isFocused)
            {
                InstantClearState();
                return;
            }
            base.OnDeselect(eventData);
        }
    }
}
