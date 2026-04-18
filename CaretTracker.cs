using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LocalContextBuilder
{
    public static class CaretTracker
    {
        public static System.Windows.Point? GetCaretPosition()
        {
            try
            {
                AutomationElement focusedElement = AutomationElement.FocusedElement;
                if (focusedElement != null)
                {
                    object patternObj;
                    if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
                    {
                        TextPattern textPattern = (TextPattern)patternObj;
                        TextPatternRange[] selection = textPattern.GetSelection();
                        if (selection != null && selection.Length > 0)
                        {
                            Rect[] rects = selection[0].GetBoundingRectangles();
                            if (rects != null && rects.Length > 0)
                            {
                                Rect caretRect = rects[0];
                                return new System.Windows.Point(caretRect.Right, caretRect.Bottom);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore UIA exceptions
            }
            return null;
        }
    }
}
