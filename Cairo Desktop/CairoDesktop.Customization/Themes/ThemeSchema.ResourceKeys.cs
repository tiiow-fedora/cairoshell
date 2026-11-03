using System;
using System.Collections.Generic;

namespace CairoDesktop.Customization.Themes
{
    public static partial class ThemeSchema
    {
        /// <summary>
        /// Every x:Key in "Cairo Desktop/Themes/Cairo.xaml". Used to catch typos in a pack's "resources".
        /// A test compares this list with the XAML file, so it can't silently drift after an upstream merge.
        /// </summary>
        public static readonly HashSet<string> ResourceKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActionCenterIcon", "CairoButtonStyle", "CairoCalendarButtonStyle", "CairoCalendarDayButtonStyle", "CairoCalendarItemStyle",
            "CairoCalendarStyle", "CairoDesktopButtonStyle", "CairoDesktopNavItemStyle", "CairoFileButtonStyle", "CairoMenuBarMainContainerStyle",
            "CairoMenuBorderStyle", "CairoMenuInnerBorderStyle", "CairoMenuItemButtonMenuStyle", "CairoMenuItemCairoMenuStyle",
            "CairoMenuItemCairoSearchMenuStyle", "CairoMenuItemContainerStyle", "CairoMessageButton", "CairoMessageInput",
            "CairoMessageText", "CairoMessageTitle", "CairoQuickLaunchButtonStyle", "CairoSearchResultsButtonStyle",
            "CairoSearchResultsItemStyle", "CairoStacksStyle", "CairoSystrayExpanderStyle", "CairoTaskbarBorderStyle",
            "CairoTaskbarButtonActiveStyle", "CairoTaskbarButtonDesktopOverlay", "CairoTaskbarButtonInactiveStyle",
            "CairoTaskbarButtonList", "CairoTaskbarFullBorderStyle", "CairoTaskbarFullButtonDesktopOverlay", "CairoTaskbarFullButtonList",
            "CairoTaskbarTopBorderStyle", "CairoTaskbarTopButtonDesktopOverlay", "CairoTaskbarTopButtonList", "CairoTaskbarTopFullBorderStyle",
            "CairoTaskbarTopFullButtonDesktopOverlay", "CairoTaskbarTopFullButtonList", "CairoTaskListButtonStyle",
            "CairoTaskListPopupStyle", "CairoWindowContainer", "CairoWindowStyle", "DateTimeIcon", "DesktopFolderViewStyle",
            "DesktopHoverBackground", "DesktopHoverBottomBorder", "DesktopHoverTopBorder", "DesktopIconText", "DesktopNavToolbar",
            "DesktopNavToolbarBackground", "DesktopNavToolbarBorder", "DesktopNavToolbarItemHoverEffect", "DesktopNavToolbarItemPressedEffect",
            "DesktopNavToolbarShadow", "DesktopOverlayIcon", "DesktopPressedBackground", "DesktopPressedBottomBorder",
            "DesktopPressedTopBorder", "DesktopToolbarBackIcon", "DesktopToolbarBrowseIcon", "DesktopToolbarForwardIcon",
            "DesktopToolbarHomeIcon", "DesktopToolbarUpIcon", "DialogButtonDefaultBorder", "DialogButtonDisabledBackground",
            "DialogButtonDisabledForeground", "DialogButtonHoverBackground", "DialogButtonHoverBorder", "DialogButtonHoverForeground",
            "DialogButtonNormalBackground", "DialogButtonNormalBorder", "DialogButtonNormalForeground", "DialogButtonPressedBackground",
            "DialogButtonPressedBorder", "DialogButtonPressedForeground", "DialogFontFamily", "DialogMessageForeground",
            "DialogWindowBackground", "DialogWindowForeground", "DisabledColor", "EnableDarkMode", "EnableInvertSystemNotifyIcons",
            "FolderViewStyle", "GlobalFontFamily", "HeaderButtonTemplate", "HorizontalScrollBar", "LargeFontSize",
            "LargeTitleFontSize", "MediumFontSize", "MediumTitleFontSize", "MenuBackgroundGradient", "MenuBarBackground",
            "MenuBarBorder", "MenuBarBorderThickness", "MenubarShadow", "MenuBarStyle", "MenuBarWindowAllowsTransparency",
            "MenuBarWindowStyle", "MenuBarWindowStyleBackground", "MenuBorderBrush", "MenuHeaderForeground", "MenuHeaderHoverBackground",
            "MenuHeaderPressedGradient", "MenuHeaderShadow", "MenuIcon", "MenuItemForeground", "MenuItemHoverGradient",
            "MenuSeparator", "MenuShadow", "MenuShadowBottom", "MenuTopBorderBrush", "NextButtonTemplate", "NoItemsForeground",
            "NoItemsTextBlock", "NotifyBalloonBackground", "NotifyIconBalloonAnimatedBorder", "NotifyIconBalloonContent",
            "NotifyIconBalloonImage", "NotifyIconBalloonInlineBorder", "NotifyIconBalloonInlineCloseButton", "NotifyIconBalloonInlinePanel",
            "NotifyIconBalloonInlineTitle", "NotifyIconBalloonPopupInnerBorder", "NotifyIconBalloonTitle", "NotifyIconBorder",
            "NotifyIconImage", "NotifyIconPanel", "NullIcon", "PreviousButtonTemplate", "ProgramsMenuButton", "ProgramsMenuCategoryForeground",
            "ProgramsMenuCategoryHoverBackground", "ProgramsTabControl", "ProgramsTabItem", "QuickLaunchListStyle",
            "ScrollBarPageButton", "ScrollBarThumb", "SearchBoxBorderStyle", "SearchIcon", "SearchResultBackground0",
            "SearchResultBackground1", "SearchResultHover", "SearchResultSubtext", "SearchResultText", "SearchTextBox",
            "SearchTextBoxClearButton", "SearchTitleBackground", "SearchTitleText", "SearchViewAllResultsBackground",
            "SearchViewAllResultsHoverBackground", "SearchViewAllResultsPressedBackground", "SmallFontSize", "StackScrollButton",
            "StackScrollViewer", "StacksIconText", "StackTopButton", "SysTrayExpanderColor", "TaskbarBottomBackground",
            "TaskbarBottomBorder", "TaskbarButtonIcon", "TaskbarButtonOverlayPanel", "TaskbarButtonTitle", "TaskbarFlashOff",
            "TaskbarFlashOn", "TaskbarGroup", "TaskbarItemActiveBackground", "TaskbarItemActiveBorder", "TaskbarItemActiveHoverBackground",
            "TaskbarItemActivePressedBackground", "TaskbarItemFlashingBackground0", "TaskbarItemFlashingBackground1",
            "TaskbarItemFlashingBackground2", "TaskbarItemFlashingBackgroundGradient", "TaskbarItemFlashingBorder0",
            "TaskbarItemFlashingBorder1", "TaskbarItemFlashingBorderGradient", "TaskbarItemFlashingDisabledBackground",
            "TaskbarItemFlashingHoverBackground", "TaskbarItemFlashingPressedBackground", "TaskbarItemForeground",
            "TaskbarItemInactiveBackground0", "TaskbarItemInactiveBackground1", "TaskbarItemInactiveBackground2", "TaskbarItemInactiveBackgroundGradient",
            "TaskbarItemInactiveBorder0", "TaskbarItemInactiveBorder1", "TaskbarItemInactiveBorderGradient", "TaskbarItemInactiveHoverBackground",
            "TaskbarItemInactiveHoverBorder", "TaskbarItemInactivePressedBackground", "TaskbarListView", "TaskbarProgressBar",
            "TaskbarProgressBarFill", "TaskbarTopBackground", "TaskbarTopBorder", "TaskBarWindowAllowsTransparency",
            "TaskBarWindowBackground", "TaskGroupBorder", "TaskListIcon", "TaskListMenuIcon", "TaskListProgressBar",
            "TaskListStyle", "TaskListTitle", "TaskThumbCloseButton", "TaskThumbCloseButtonForeground", "TaskThumbCloseButtonHoverBackground",
            "TaskThumbCloseButtonPressedBackground", "TaskThumbHoverBackground", "TaskThumbInactiveBackground", "TaskThumbTitleStyle",
            "TaskThumbWindowBorderStyle", "TaskThumbWindowBorderTopStyle", "TaskThumbWindowInnerBorderStyle", "TaskThumbWindowInnerBorderTopStyle",
            "TaskThumbWindowInnerStyle", "TaskViewIcon", "VerticalScrollBar", "VolumeIcon", "VolumeLowIcon", "VolumeMuteIcon",
            "VolumeOffIcon", "WelcomeHeading", "WelcomeSubHeading", "WelcomeSubText", "WelcomeText", "WizardText",
        };
    }
}
