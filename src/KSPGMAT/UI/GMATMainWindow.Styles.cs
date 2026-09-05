using System;
using UnityEngine;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void InitStyles() {
            if (stylesInitialized) return;

            // Deep Space Dark Theme Palettes (Zero blinding white flashes when focused)
            Texture2D darkBg = MakeBoxTex(8, 8, new Color(0.038f, 0.048f, 0.070f, 0.98f), new Color(0.16f, 0.20f, 0.26f, 1.0f), 1);
            Texture2D darkFocusedBg = MakeBoxTex(8, 8, new Color(0.038f, 0.048f, 0.070f, 0.98f), new Color(0.16f, 0.20f, 0.26f, 1.0f), 1);
            Texture2D cardBg = MakeBoxTex(8, 8, new Color(0.065f, 0.082f, 0.120f, 0.94f), new Color(0.15f, 0.19f, 0.26f, 0.9f), 1);
            Texture2D nestedCardBg = MakeBoxTex(8, 8, new Color(0.045f, 0.060f, 0.095f, 0.95f), new Color(0.15f, 0.28f, 0.42f, 0.85f), 1);
            
            Texture2D activeTabBg = MakeBoxTex(8, 8, new Color(0.02f, 0.35f, 0.55f, 1.0f), new Color(0.22f, 0.74f, 0.97f, 1.0f), 1);
            Texture2D hoverTabBg = MakeBoxTex(8, 8, new Color(0.09f, 0.12f, 0.18f, 1.0f), new Color(0.22f, 0.32f, 0.44f, 1.0f), 1);
            
            Texture2D actionBtnBg = MakeBoxTex(8, 8, new Color(0.01f, 0.42f, 0.65f, 1.0f), new Color(0.22f, 0.74f, 0.97f, 1.0f), 1);
            Texture2D actionBtnHoverBg = MakeBoxTex(8, 8, new Color(0.02f, 0.48f, 0.74f, 1.0f), new Color(0.38f, 0.82f, 1.0f, 1.0f), 1);
            Texture2D actionBtnActiveBg = MakeBoxTex(8, 8, new Color(0.01f, 0.32f, 0.50f, 1.0f), new Color(0.18f, 0.60f, 0.82f, 1.0f), 1);

            Texture2D successBtnBg = MakeBoxTex(8, 8, new Color(0.06f, 0.42f, 0.24f, 1.0f), new Color(0.20f, 0.75f, 0.42f, 1.0f), 1);
            Texture2D successBtnHoverBg = MakeBoxTex(8, 8, new Color(0.08f, 0.50f, 0.28f, 1.0f), new Color(0.28f, 0.85f, 0.50f, 1.0f), 1);

            Texture2D deleteBtnBg = MakeBoxTex(8, 8, new Color(0.55f, 0.14f, 0.14f, 1.0f), new Color(0.85f, 0.30f, 0.30f, 1.0f), 1);
            Texture2D deleteBtnHoverBg = MakeBoxTex(8, 8, new Color(0.68f, 0.18f, 0.18f, 1.0f), new Color(0.98f, 0.40f, 0.40f, 1.0f), 1);

            Texture2D secondaryBtnBg = MakeBoxTex(8, 8, new Color(0.09f, 0.12f, 0.17f, 1.0f), new Color(0.20f, 0.26f, 0.36f, 1.0f), 1);
            Texture2D secondaryBtnHoverBg = MakeBoxTex(8, 8, new Color(0.13f, 0.17f, 0.24f, 1.0f), new Color(0.26f, 0.36f, 0.50f, 1.0f), 1);
            Texture2D secondaryBtnActiveBg = MakeBoxTex(8, 8, new Color(0.06f, 0.09f, 0.13f, 1.0f), new Color(0.16f, 0.20f, 0.28f, 1.0f), 1);

            Texture2D inputBg = MakeBoxTex(8, 8, new Color(0.055f, 0.075f, 0.115f, 0.95f), new Color(0.16f, 0.22f, 0.30f, 1.0f), 1);
            Texture2D inputFocusBg = MakeBoxTex(8, 8, new Color(0.075f, 0.105f, 0.155f, 0.98f), new Color(0.22f, 0.28f, 0.38f, 1.0f), 1);

            // Refined Balanced Step Pill Backgrounds (Soft, low-fatigue tones)
            Texture2D pillVaryBg = MakeBoxTex(8, 8, new Color(0.05f, 0.16f, 0.26f, 0.95f), new Color(0.12f, 0.42f, 0.65f, 0.9f), 1);
            Texture2D pillAchieveBg = MakeBoxTex(8, 8, new Color(0.05f, 0.18f, 0.12f, 0.95f), new Color(0.12f, 0.48f, 0.32f, 0.9f), 1);
            Texture2D pillBurnBg = MakeBoxTex(8, 8, new Color(0.24f, 0.12f, 0.05f, 0.95f), new Color(0.65f, 0.35f, 0.12f, 0.9f), 1);
            Texture2D pillImpulseBg = MakeBoxTex(8, 8, new Color(0.18f, 0.08f, 0.24f, 0.95f), new Color(0.50f, 0.22f, 0.65f, 0.9f), 1);
            Texture2D pillPropBg = MakeBoxTex(8, 8, new Color(0.24f, 0.08f, 0.10f, 0.95f), new Color(0.62f, 0.18f, 0.24f, 0.9f), 1);
            Texture2D pillStageBg = MakeBoxTex(8, 8, new Color(0.22f, 0.16f, 0.05f, 0.95f), new Color(0.60f, 0.45f, 0.12f, 0.9f), 1);
            Texture2D pillTargetBg = MakeBoxTex(8, 8, new Color(0.06f, 0.14f, 0.24f, 0.95f), new Color(0.18f, 0.40f, 0.65f, 0.9f), 1);
            Texture2D pillEndTargetBg = MakeBoxTex(8, 8, new Color(0.08f, 0.11f, 0.16f, 0.95f), new Color(0.20f, 0.28f, 0.38f, 0.9f), 1);

            // 1. Window Style (Crucial: set onNormal, onFocused, focused, active to prevent Unity white glow)
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = darkBg;
            windowStyle.onNormal.background = darkFocusedBg;
            windowStyle.hover.background = darkBg;
            windowStyle.onHover.background = darkFocusedBg;
            windowStyle.active.background = darkFocusedBg;
            windowStyle.onActive.background = darkFocusedBg;
            windowStyle.focused.background = darkFocusedBg;
            windowStyle.onFocused.background = darkFocusedBg;
            windowStyle.normal.textColor = Color.white;
            windowStyle.onNormal.textColor = Color.white;
            windowStyle.border = new RectOffset(3, 3, 3, 3);
            windowStyle.fontSize = 13;
            windowStyle.fontStyle = FontStyle.Bold;

            // Also override GUI.skin.window so child windows don't turn white
            GUI.skin.window.normal.background = darkBg;
            GUI.skin.window.onNormal.background = darkFocusedBg;
            GUI.skin.window.hover.background = darkBg;
            GUI.skin.window.onHover.background = darkFocusedBg;
            GUI.skin.window.active.background = darkFocusedBg;
            GUI.skin.window.onActive.background = darkFocusedBg;
            GUI.skin.window.focused.background = darkFocusedBg;
            GUI.skin.window.onFocused.background = darkFocusedBg;

            // 2. Global Textfield & TextArea Styles (Prevents bright blue/white glow when clicking inputs)
            GUI.skin.textField.normal.background = inputBg;
            GUI.skin.textField.normal.textColor = new Color(0.92f, 0.95f, 0.98f);
            GUI.skin.textField.hover.background = inputFocusBg;
            GUI.skin.textField.hover.textColor = Color.white;
            GUI.skin.textField.focused.background = inputFocusBg;
            GUI.skin.textField.focused.textColor = Color.white;
            GUI.skin.textField.active.background = inputFocusBg;
            GUI.skin.textField.active.textColor = Color.white;
            GUI.skin.textField.onNormal.background = inputFocusBg;
            GUI.skin.textField.onFocused.background = inputFocusBg;
            GUI.skin.textField.onHover.background = inputFocusBg;
            GUI.skin.textField.onActive.background = inputFocusBg;
            GUI.skin.textField.border = new RectOffset(2, 2, 2, 2);
            GUI.skin.textField.padding = new RectOffset(6, 6, 2, 2);
            GUI.skin.textField.fontSize = 11;

            Texture2D textAreaBg = MakeBoxTex(4, 4, new Color(0.028f, 0.038f, 0.058f, 0.98f), new Color(0.14f, 0.20f, 0.30f, 0.9f), 1);
            Texture2D textAreaFocusBg = MakeBoxTex(4, 4, new Color(0.032f, 0.044f, 0.068f, 0.98f), new Color(0.22f, 0.28f, 0.38f, 1.0f), 1);

            GUI.skin.textArea.normal.background = textAreaBg;
            GUI.skin.textArea.normal.textColor = new Color(0.92f, 0.96f, 1.0f);
            GUI.skin.textArea.hover.background = textAreaBg;
            GUI.skin.textArea.hover.textColor = Color.white;
            GUI.skin.textArea.focused.background = textAreaFocusBg;
            GUI.skin.textArea.focused.textColor = Color.white;
            GUI.skin.textArea.active.background = textAreaFocusBg;
            GUI.skin.textArea.active.textColor = Color.white;
            GUI.skin.textArea.onNormal.background = textAreaBg;
            GUI.skin.textArea.onNormal.textColor = new Color(0.92f, 0.96f, 1.0f);
            GUI.skin.textArea.onHover.background = textAreaBg;
            GUI.skin.textArea.onHover.textColor = Color.white;
            GUI.skin.textArea.onFocused.background = textAreaFocusBg;
            GUI.skin.textArea.onFocused.textColor = Color.white;
            GUI.skin.textArea.onActive.background = textAreaFocusBg;
            GUI.skin.textArea.onActive.textColor = Color.white;
            GUI.skin.textArea.border = new RectOffset(2, 2, 2, 2);

            // 3. Card Box Style
            cardBoxStyle = new GUIStyle(GUI.skin.box);
            cardBoxStyle.normal.background = cardBg;
            cardBoxStyle.onNormal.background = cardBg;
            cardBoxStyle.hover.background = cardBg;
            cardBoxStyle.onHover.background = cardBg;
            cardBoxStyle.active.background = cardBg;
            cardBoxStyle.onActive.background = cardBg;
            cardBoxStyle.focused.background = cardBg;
            cardBoxStyle.onFocused.background = cardBg;
            cardBoxStyle.border = new RectOffset(1, 1, 1, 1); // 基线4.2: 卡片外边框 1px 微光细线
            cardBoxStyle.padding = new RectOffset(8, 8, 8, 8);

            nestedCardBoxStyle = new GUIStyle(GUI.skin.box);
            nestedCardBoxStyle.normal.background = nestedCardBg;
            nestedCardBoxStyle.onNormal.background = nestedCardBg;
            nestedCardBoxStyle.hover.background = nestedCardBg;
            nestedCardBoxStyle.onHover.background = nestedCardBg;
            nestedCardBoxStyle.active.background = nestedCardBg;
            nestedCardBoxStyle.onActive.background = nestedCardBg;
            nestedCardBoxStyle.focused.background = nestedCardBg;
            nestedCardBoxStyle.onFocused.background = nestedCardBg;
            nestedCardBoxStyle.border = new RectOffset(2, 2, 2, 2);
            nestedCardBoxStyle.padding = new RectOffset(8, 8, 8, 8);

            // Prominent Group Bounding Box (High-contrast 2px cyan border enclosing entire group)
            Texture2D groupBorderBg = MakeBoxTex(8, 8, new Color(0.035f, 0.052f, 0.085f, 0.96f), new Color(0.12f, 0.55f, 0.90f, 0.95f), 2);
            targetGroupBoxStyle = new GUIStyle(GUI.skin.box);
            targetGroupBoxStyle.normal.background = groupBorderBg;
            targetGroupBoxStyle.onNormal.background = groupBorderBg;
            targetGroupBoxStyle.hover.background = groupBorderBg;
            targetGroupBoxStyle.onHover.background = groupBorderBg;
            targetGroupBoxStyle.active.background = groupBorderBg;
            targetGroupBoxStyle.onActive.background = groupBorderBg;
            targetGroupBoxStyle.focused.background = groupBorderBg;
            targetGroupBoxStyle.onFocused.background = groupBorderBg;
            targetGroupBoxStyle.border = new RectOffset(3, 3, 3, 3);
            targetGroupBoxStyle.padding = new RectOffset(8, 8, 8, 8);

            // Dynamic Interactive Glow Styles for Step Cards
            Texture2D groupHoverBg = MakeBoxTex(8, 8, new Color(0.045f, 0.070f, 0.115f, 0.98f), new Color(0.38f, 0.75f, 1.0f, 1.0f), 2);
            targetGroupHoverBoxStyle = new GUIStyle(targetGroupBoxStyle);
            targetGroupHoverBoxStyle.normal.background = groupHoverBg;
            targetGroupHoverBoxStyle.hover.background = groupHoverBg;

            Texture2D stepHoverBg = MakeBoxTex(8, 8, new Color(0.055f, 0.085f, 0.135f, 0.98f), new Color(0.22f, 0.74f, 0.97f, 0.95f), 1);
            stepHoverBoxStyle = new GUIStyle(nestedCardBoxStyle);
            stepHoverBoxStyle.normal.background = stepHoverBg;
            stepHoverBoxStyle.hover.background = stepHoverBg;

            Texture2D stepSolvingBg = MakeBoxTex(8, 8, new Color(0.04f, 0.10f, 0.07f, 0.98f), new Color(0.20f, 0.85f, 0.55f, 0.95f), 1);
            stepSolvingBoxStyle = new GUIStyle(nestedCardBoxStyle);
            stepSolvingBoxStyle.normal.background = stepSolvingBg;
            stepSolvingBoxStyle.hover.background = stepSolvingBg;

            Texture2D stepDraggingBg = MakeBoxTex(8, 8, new Color(0.12f, 0.09f, 0.03f, 0.98f), new Color(0.98f, 0.80f, 0.12f, 0.95f), 2);
            stepDraggingBoxStyle = new GUIStyle(nestedCardBoxStyle);
            stepDraggingBoxStyle.normal.background = stepDraggingBg;
            stepDraggingBoxStyle.hover.background = stepDraggingBg;

            // 4. Tab Button Style
            tabButtonStyle = new GUIStyle(GUI.skin.button);
            tabButtonStyle.normal.background = cardBg;
            tabButtonStyle.normal.textColor = new Color(0.72f, 0.78f, 0.86f);
            tabButtonStyle.hover.background = hoverTabBg;
            tabButtonStyle.hover.textColor = Color.white;
            tabButtonStyle.active.background = activeTabBg;
            tabButtonStyle.active.textColor = Color.white;
            tabButtonStyle.focused.background = cardBg;
            tabButtonStyle.onNormal.background = cardBg;
            tabButtonStyle.onHover.background = hoverTabBg;
            tabButtonStyle.onActive.background = activeTabBg;
            tabButtonStyle.onFocused.background = cardBg;
            tabButtonStyle.border = new RectOffset(2, 2, 2, 2);
            tabButtonStyle.fontSize = 11;
            tabButtonStyle.fontStyle = FontStyle.Bold;

            tabButtonActiveStyle = new GUIStyle(tabButtonStyle);
            tabButtonActiveStyle.normal.background = activeTabBg;
            tabButtonActiveStyle.normal.textColor = Color.white;
            tabButtonActiveStyle.hover.background = activeTabBg;
            tabButtonActiveStyle.hover.textColor = Color.white;
            tabButtonActiveStyle.active.background = activeTabBg;
            tabButtonActiveStyle.active.textColor = Color.white;
            tabButtonActiveStyle.focused.background = activeTabBg;
            tabButtonActiveStyle.onNormal.background = activeTabBg;
            tabButtonActiveStyle.onHover.background = activeTabBg;
            tabButtonActiveStyle.onActive.background = activeTabBg;
            tabButtonActiveStyle.onFocused.background = activeTabBg;
            tabButtonActiveStyle.border = new RectOffset(2, 2, 2, 2);

            // 5. Action Buttons
            actionButtonStyle = new GUIStyle(GUI.skin.button);
            actionButtonStyle.normal.background = actionBtnBg;
            actionButtonStyle.normal.textColor = Color.white;
            actionButtonStyle.hover.background = actionBtnHoverBg;
            actionButtonStyle.hover.textColor = Color.white;
            actionButtonStyle.active.background = actionBtnActiveBg;
            actionButtonStyle.active.textColor = Color.white;
            actionButtonStyle.focused.background = actionBtnBg;
            actionButtonStyle.onNormal.background = actionBtnBg;
            actionButtonStyle.onFocused.background = actionBtnBg;
            actionButtonStyle.border = new RectOffset(2, 2, 2, 2);
            actionButtonStyle.fontSize = 12;
            actionButtonStyle.fontStyle = FontStyle.Bold;

            successButtonStyle = new GUIStyle(GUI.skin.button);
            successButtonStyle.normal.background = successBtnBg;
            successButtonStyle.normal.textColor = Color.white;
            successButtonStyle.hover.background = successBtnHoverBg;
            successButtonStyle.hover.textColor = Color.white;
            successButtonStyle.active.background = successBtnBg;
            successButtonStyle.active.textColor = Color.white;
            successButtonStyle.focused.background = successBtnBg;
            successButtonStyle.onNormal.background = successBtnBg;
            successButtonStyle.onFocused.background = successBtnBg;
            successButtonStyle.border = new RectOffset(2, 2, 2, 2);
            successButtonStyle.fontSize = 12;
            successButtonStyle.fontStyle = FontStyle.Bold;

            secondaryButtonStyle = new GUIStyle(GUI.skin.button);
            secondaryButtonStyle.normal.background = secondaryBtnBg;
            secondaryButtonStyle.normal.textColor = new Color(0.40f, 0.82f, 1.0f);
            secondaryButtonStyle.hover.background = secondaryBtnHoverBg;
            secondaryButtonStyle.hover.textColor = new Color(0.60f, 0.90f, 1.0f);
            secondaryButtonStyle.active.background = secondaryBtnActiveBg;
            secondaryButtonStyle.active.textColor = Color.white;
            secondaryButtonStyle.focused.background = secondaryBtnBg;
            secondaryButtonStyle.onNormal.background = secondaryBtnBg;
            secondaryButtonStyle.onFocused.background = secondaryBtnBg;
            secondaryButtonStyle.border = new RectOffset(2, 2, 2, 2);
            secondaryButtonStyle.fontSize = 11;

            deleteButtonStyle = new GUIStyle(GUI.skin.button);
            deleteButtonStyle.normal.background = deleteBtnBg;
            deleteButtonStyle.normal.textColor = Color.white;
            deleteButtonStyle.hover.background = deleteBtnHoverBg;
            deleteButtonStyle.hover.textColor = Color.white;
            deleteButtonStyle.active.background = deleteBtnBg;
            deleteButtonStyle.active.textColor = Color.white;
            deleteButtonStyle.focused.background = deleteBtnBg;
            deleteButtonStyle.onNormal.background = deleteBtnBg;
            deleteButtonStyle.onFocused.background = deleteBtnBg;
            deleteButtonStyle.border = new RectOffset(2, 2, 2, 2);
            deleteButtonStyle.fontSize = 11;
            deleteButtonStyle.fontStyle = FontStyle.Bold;

            // 6. Labels & Tooltips
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 12;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(0.40f, 0.82f, 1.0f);

            subHeaderStyle = new GUIStyle(GUI.skin.label);
            subHeaderStyle.fontSize = 11;
            subHeaderStyle.fontStyle = FontStyle.Bold;
            subHeaderStyle.normal.textColor = new Color(0.85f, 0.85f, 0.60f);

            labelValueStyle = new GUIStyle(GUI.skin.label);
            labelValueStyle.fontSize = 11;
            labelValueStyle.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
            labelValueStyle.richText = true; // 支持 I18n.ColorFormat 的 <color> 数值着色

            tooltipStyle = new GUIStyle(GUI.skin.box);
            tooltipStyle.normal.background = MakeBoxTex(8, 8, new Color(0.04f, 0.05f, 0.08f, 0.98f), new Color(0.24f, 0.65f, 0.95f, 0.8f), 1);
            tooltipStyle.normal.textColor = new Color(0.92f, 0.95f, 1.0f);
            tooltipStyle.border = new RectOffset(2, 2, 2, 2);
            tooltipStyle.fontSize = 11;
            tooltipStyle.wordWrap = true;
            tooltipStyle.padding = new RectOffset(10, 10, 8, 8);
            tooltipStyle.alignment = TextAnchor.UpperLeft;

            // 7. Modern Step Pill Badges (Balanced, readable chips)
            pillPropStyle = new GUIStyle(GUI.skin.box);
            pillPropStyle.normal.background = pillPropBg;
            pillPropStyle.normal.textColor = new Color(0.96f, 0.60f, 0.65f);
            pillPropStyle.fontStyle = FontStyle.Bold;
            pillPropStyle.fontSize = 10;
            pillPropStyle.padding = new RectOffset(6, 6, 2, 2);
            pillPropStyle.alignment = TextAnchor.MiddleCenter;

            pillVaryStyle = new GUIStyle(GUI.skin.box);
            pillVaryStyle.normal.background = pillVaryBg;
            pillVaryStyle.normal.textColor = new Color(0.55f, 0.82f, 0.98f);
            pillVaryStyle.fontStyle = FontStyle.Bold;
            pillVaryStyle.fontSize = 10;
            pillVaryStyle.padding = new RectOffset(6, 6, 2, 2);
            pillVaryStyle.alignment = TextAnchor.MiddleCenter;

            pillAchieveStyle = new GUIStyle(GUI.skin.box);
            pillAchieveStyle.normal.background = pillAchieveBg;
            pillAchieveStyle.normal.textColor = new Color(0.50f, 0.90f, 0.70f);
            pillAchieveStyle.fontStyle = FontStyle.Bold;
            pillAchieveStyle.fontSize = 10;
            pillAchieveStyle.padding = new RectOffset(6, 6, 2, 2);
            pillAchieveStyle.alignment = TextAnchor.MiddleCenter;

            pillBurnStyle = new GUIStyle(GUI.skin.box);
            pillBurnStyle.normal.background = pillBurnBg;
            pillBurnStyle.normal.textColor = new Color(0.98f, 0.75f, 0.50f);
            pillBurnStyle.fontStyle = FontStyle.Bold;
            pillBurnStyle.fontSize = 10;
            pillBurnStyle.padding = new RectOffset(6, 6, 2, 2);
            pillBurnStyle.alignment = TextAnchor.MiddleCenter;

            pillImpulseStyle = new GUIStyle(GUI.skin.box);
            pillImpulseStyle.normal.background = pillImpulseBg;
            pillImpulseStyle.normal.textColor = new Color(0.85f, 0.70f, 0.98f);
            pillImpulseStyle.fontStyle = FontStyle.Bold;
            pillImpulseStyle.fontSize = 10;
            pillImpulseStyle.padding = new RectOffset(6, 6, 2, 2);
            pillImpulseStyle.alignment = TextAnchor.MiddleCenter;

            pillStageStyle = new GUIStyle(GUI.skin.box);
            pillStageStyle.normal.background = pillStageBg;
            pillStageStyle.normal.textColor = new Color(0.95f, 0.85f, 0.45f);
            pillStageStyle.fontStyle = FontStyle.Bold;
            pillStageStyle.fontSize = 10;
            pillStageStyle.padding = new RectOffset(6, 6, 2, 2);
            pillStageStyle.alignment = TextAnchor.MiddleCenter;

            pillTargetStyle = new GUIStyle(GUI.skin.box);
            pillTargetStyle.normal.background = pillTargetBg;
            pillTargetStyle.normal.textColor = new Color(0.60f, 0.85f, 1.0f);
            pillTargetStyle.fontStyle = FontStyle.Bold;
            pillTargetStyle.fontSize = 10;
            pillTargetStyle.padding = new RectOffset(6, 6, 2, 2);
            pillTargetStyle.alignment = TextAnchor.MiddleCenter;

            pillEndTargetStyle = new GUIStyle(GUI.skin.box);
            pillEndTargetStyle.normal.background = pillEndTargetBg;
            pillEndTargetStyle.normal.textColor = new Color(0.70f, 0.78f, 0.88f);
            pillEndTargetStyle.fontStyle = FontStyle.Bold;
            pillEndTargetStyle.fontSize = 10;
            pillEndTargetStyle.padding = new RectOffset(6, 6, 2, 2);
            pillEndTargetStyle.alignment = TextAnchor.MiddleCenter;

            pillGenericStyle = new GUIStyle(GUI.skin.box);
            pillGenericStyle.normal.background = secondaryBtnBg;
            pillGenericStyle.normal.textColor = new Color(0.65f, 0.75f, 0.88f);
            pillGenericStyle.fontSize = 10;
            pillGenericStyle.padding = new RectOffset(6, 6, 2, 2);
            pillGenericStyle.alignment = TextAnchor.MiddleCenter;

            // 8. Numerical & Grid Styles
            gridLabelStyle = new GUIStyle(GUI.skin.label);
            gridLabelStyle.fontSize = 11;
            gridLabelStyle.normal.textColor = new Color(0.58f, 0.66f, 0.78f);
            gridLabelStyle.alignment = TextAnchor.MiddleLeft;

            sectionHeaderStyle = new GUIStyle(GUI.skin.label);
            sectionHeaderStyle.fontSize = 11;
            sectionHeaderStyle.fontStyle = FontStyle.Bold;
            sectionHeaderStyle.normal.textColor = new Color(0.24f, 0.70f, 0.95f);

            badgeDarkStyle = new GUIStyle(GUI.skin.box);
            badgeDarkStyle.normal.background = MakeBoxTex(4, 4, new Color(0.04f, 0.06f, 0.09f, 0.9f), new Color(0.14f, 0.22f, 0.32f, 0.8f), 1);
            badgeDarkStyle.normal.textColor = new Color(0.95f, 0.82f, 0.30f);
            badgeDarkStyle.fontSize = 10;
            badgeDarkStyle.padding = new RectOffset(5, 5, 2, 2);
            badgeDarkStyle.alignment = TextAnchor.MiddleLeft;

            numericInputStyle = new GUIStyle(GUI.skin.textField);
            numericInputStyle.normal.background = inputBg;
            numericInputStyle.focused.background = inputFocusBg;
            numericInputStyle.hover.background = inputFocusBg;
            numericInputStyle.active.background = inputFocusBg;
            numericInputStyle.normal.textColor = Color.white;
            numericInputStyle.focused.textColor = Color.white;
            numericInputStyle.fontSize = 11;
            numericInputStyle.alignment = TextAnchor.MiddleRight;
            numericInputStyle.padding = new RectOffset(6, 6, 2, 2);
            numericInputStyle.border = new RectOffset(2, 2, 2, 2);

            stepLabelInputStyle = new GUIStyle(GUI.skin.textField);
            stepLabelInputStyle.normal.background = inputBg;
            stepLabelInputStyle.focused.background = inputFocusBg;
            stepLabelInputStyle.hover.background = inputFocusBg;
            stepLabelInputStyle.active.background = inputFocusBg;
            stepLabelInputStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);
            stepLabelInputStyle.focused.textColor = Color.white;
            stepLabelInputStyle.fontSize = 11;
            stepLabelInputStyle.alignment = TextAnchor.MiddleLeft;
            stepLabelInputStyle.padding = new RectOffset(6, 6, 2, 2);
            stepLabelInputStyle.border = new RectOffset(2, 2, 2, 2);

            // 9. Unified Clean Dark Slate Step Box Styles (No neon borders; cards share sleek cohesive look)
            stepBoxPropStyle = nestedCardBoxStyle;
            stepBoxVaryStyle = nestedCardBoxStyle;
            stepBoxAchieveStyle = nestedCardBoxStyle;
            stepBoxBurnStyle = nestedCardBoxStyle;
            stepBoxImpulseStyle = nestedCardBoxStyle;
            stepBoxStageStyle = nestedCardBoxStyle;
            stepBoxReportStyle = nestedCardBoxStyle;
            stepBoxEndTargetStyle = nestedCardBoxStyle;

            // 10. Clean Target Group Box (1px subtle slate border with dark nested backdrop)
            targetGroupBoxStyle = new GUIStyle(GUI.skin.box);
            targetGroupBoxStyle.normal.background = MakeBoxTex(8, 8, new Color(0.028f, 0.040f, 0.062f, 0.98f), new Color(0.18f, 0.26f, 0.38f, 0.90f), 1);
            targetGroupBoxStyle.border = new RectOffset(3, 3, 3, 3);
            targetGroupBoxStyle.padding = new RectOffset(8, 8, 8, 8);

            codeBlockStyle = new GUIStyle(GUI.skin.textArea) {
                fontSize = 11,
                font = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Cascadia Code", "Courier New" }, 11),
                wordWrap = false,
                richText = true
            };
            codeBlockStyle.richText = true;
            codeBlockStyle.normal.textColor = new Color(0.85f, 0.90f, 0.95f, 1f);
            codeBlockStyle.normal.background = textAreaBg;
            codeBlockStyle.hover.background = textAreaBg;
            codeBlockStyle.focused.background = textAreaFocusBg;
            codeBlockStyle.active.background = textAreaFocusBg;
            codeBlockStyle.onNormal.background = textAreaBg;
            codeBlockStyle.onHover.background = textAreaBg;
            codeBlockStyle.onFocused.background = textAreaFocusBg;
            codeBlockStyle.onActive.background = textAreaFocusBg;
            codeBlockStyle.border = new RectOffset(2, 2, 2, 2);

            ideEditorStyle = new GUIStyle(GUI.skin.textArea) {
                fontSize = 11,
                font = Font.CreateDynamicFontFromOSFont(new string[] { "Consolas", "Cascadia Code", "Courier New" }, 11),
                wordWrap = false,
                richText = false
            };
            ideEditorStyle.normal.textColor = new Color(0.92f, 0.96f, 1.0f, 1f);
            ideEditorStyle.hover.textColor = new Color(0.95f, 0.98f, 1.0f, 1f);
            ideEditorStyle.focused.textColor = Color.white;
            ideEditorStyle.active.textColor = Color.white;
            ideEditorStyle.normal.background = textAreaBg;
            ideEditorStyle.hover.background = textAreaBg;
            ideEditorStyle.focused.background = textAreaFocusBg;
            ideEditorStyle.active.background = textAreaFocusBg;
            ideEditorStyle.onNormal.background = textAreaBg;
            ideEditorStyle.onHover.background = textAreaBg;
            ideEditorStyle.onFocused.background = textAreaFocusBg;
            ideEditorStyle.onActive.background = textAreaFocusBg;
            ideEditorStyle.padding = new RectOffset(8, 8, 6, 6);
            ideEditorStyle.border = new RectOffset(2, 2, 2, 2);

            stylesInitialized = true;
        }

        private GUIStyle GetTargetGroupBoxStyle(int groupIndex, bool isHovered = false) {
            if (isHovered && targetGroupHoverBoxStyle != null) return targetGroupHoverBoxStyle;
            return targetGroupBoxStyle ?? cardBoxStyle;
        }

        private GUIStyle GetStepBoxStyle(string stepType, bool isHovered = false, bool isSolving = false, bool isBeingDragged = false) {
            if (isBeingDragged && stepDraggingBoxStyle != null) return stepDraggingBoxStyle;
            if (isSolving && stepSolvingBoxStyle != null) return stepSolvingBoxStyle;
            if (isHovered && stepHoverBoxStyle != null) return stepHoverBoxStyle;
            return nestedCardBoxStyle;
        }

        public static Color GetStepThemeColor(string stepType) {
            if (string.IsNullOrEmpty(stepType)) return new Color(0.58f, 0.64f, 0.72f, 1f);
            switch (stepType) {
                case "Propagate":
                    return new Color(0.96f, 0.38f, 0.45f, 1f); // #f43f5e Coral Red (100% Matches pillPropStyle)
                case "FiniteBurn":
                case "BeginFiniteBurn":
                case "EndFiniteBurn":
                    return new Color(0.96f, 0.62f, 0.04f, 1f); // #f59e0b Amber Flame (100% Matches pillBurnStyle)
                case "ImpulsiveBurn":
                case "Maneuver":
                    return new Color(0.78f, 0.48f, 0.98f, 1f); // #c084fc Purple / Violet (100% Matches pillImpulseStyle)
                case "Vary":
                case "VaryThrustDirection":
                    return new Color(0.22f, 0.74f, 0.97f, 1f); // #38bdf8 Sky Blue (100% Matches pillVaryStyle)
                case "Achieve":
                    return new Color(0.20f, 0.83f, 0.60f, 1f); // #34d399 Emerald Green (100% Matches pillAchieveStyle)
                case "Target":
                    return new Color(0.39f, 0.40f, 0.95f, 1f); // #6366f1 Indigo (100% Matches pillTargetStyle)
                case "EndTarget":
                    return new Color(0.60f, 0.68f, 0.78f, 1f); // #94a3b8 Slate (100% Matches pillEndTargetStyle)
                case "StageSeparation":
                    return new Color(0.95f, 0.85f, 0.45f, 1f); // #facc15 Gold Yellow (100% Matches pillStageStyle)
                case "ThrustVectorControl":
                    return new Color(0.96f, 0.62f, 0.04f, 1f); // #f59e0b Amber Flame (与 pillBurnStyle 一致, 基线4.3)
                case "Equation":
                case "Set":
                    return new Color(0.38f, 0.82f, 1.0f, 1f);  // Cyan
                case "Report":
                    return new Color(0.65f, 0.75f, 0.88f, 1f); // Steel
                default:
                    return new Color(0.38f, 0.82f, 1.0f, 1f);
            }
        }

        private Texture2D MakeBoxTex(int width, int height, Color fill, Color border, int borderWidth = 1) {
            Color[] pix = new Color[width * height];
            for (int y = 0; y < height; ++y) {
                for (int x = 0; x < width; ++x) {
                    bool isBorder = (x < borderWidth || x >= width - borderWidth || y < borderWidth || y >= height - borderWidth);
                    pix[y * width + x] = isBorder ? border : fill;
                }
            }
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
