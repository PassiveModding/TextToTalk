﻿using System;
using System.Linq;
using Dalamud.Data;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.UI;
using TextToTalk.Backends;
using TextToTalk.Talk;

namespace TextToTalk.Middleware;

public class BattleTalkAddonHandler
{
    private readonly ClientState clientState;
    private readonly GameGui gui;
    private readonly DataManager data;
    private readonly MessageHandlerFilters filters;
    private readonly ObjectTable objects;
    private readonly Condition condition;
    private readonly PluginConfiguration config;
    private readonly SharedState sharedState;
    private readonly VoiceBackendManager backendManager;

    public Action<GameObject, string, TextSource> Say { get; set; }

    public BattleTalkAddonHandler(ClientState clientState, GameGui gui, DataManager data, MessageHandlerFilters filters,
        ObjectTable objects, Condition condition, PluginConfiguration config, SharedState sharedState,
        VoiceBackendManager backendManager)
    {
        this.clientState = clientState;
        this.gui = gui;
        this.data = data;
        this.filters = filters;
        this.objects = objects;
        this.condition = condition;
        this.config = config;
        this.sharedState = sharedState;
        this.backendManager = backendManager;
    }

    public unsafe void ShowBattleTalk(string name, string text, float duration, byte style)
    {
        var ui = (UIModule*)this.gui.GetUIModule();
        ui->ShowBattleTalk(name, text, duration, style);
    }

    public unsafe void PollAddon(PollSource pollSource)
    {
        if (!this.clientState.IsLoggedIn || this.condition[ConditionFlag.CreatingCharacter])
        {
            this.sharedState.BattleTalkAddon = nint.Zero;
            return;
        }

        if (this.sharedState.BattleTalkAddon == nint.Zero)
        {
            this.sharedState.BattleTalkAddon = this.gui.GetAddonByName("_BattleTalk");
            if (this.sharedState.BattleTalkAddon == nint.Zero) return;
        }

        var battleTalkAddon = (AddonBattleTalk*)this.sharedState.BattleTalkAddon.ToPointer();
        if (battleTalkAddon == null) return;

        if (!TalkUtils.IsVisible(battleTalkAddon))
        {
            // Cancel TTS when the dialogue window is closed, if configured
            if (this.config.CancelSpeechOnTextAdvance)
            {
                this.backendManager.CancelSay(TextSource.TalkAddon);
            }

            this.filters.SetLastBattleText("");
            return;
        }

        TalkAddonText talkAddonText;
        try
        {
            talkAddonText = TalkUtils.ReadTalkAddon(this.data, battleTalkAddon);
        }
        catch (NullReferenceException)
        {
            // Just swallow the NRE, I have no clue what causes this but it only happens when relogging in rare cases
            return;
        }

        var text = TalkUtils.NormalizePunctuation(talkAddonText.Text);
        if (text == "" || this.filters.IsDuplicateBattleText(text)) return;
        this.filters.SetLastBattleText(text);
        PluginLog.LogDebug($"AddonBattleTalk: \"{text}\"");

        if (pollSource == PollSource.VoiceLinePlayback && this.config.SkipVoicedQuestText)
        {
            PluginLog.Log($"Skipping voice-acted line: {text}");
            return;
        }

        if (talkAddonText.Speaker != "" && this.filters.ShouldSaySender())
        {
            if (!this.config.DisallowMultipleSay || !this.filters.IsSameSpeaker(talkAddonText.Speaker))
            {
                var speakerNameToSay = talkAddonText.Speaker;

                if (config.SayPartialName)
                {
                    speakerNameToSay = TalkUtils.GetPartialName(speakerNameToSay, config.OnlySayFirstOrLastName);
                }

                text = $"{speakerNameToSay} says {text}";
                this.filters.SetLastSpeaker(talkAddonText.Speaker);
            }
        }

        var speaker = this.objects.FirstOrDefault(gObj => gObj.Name.TextValue == talkAddonText.Speaker);
        if (!this.filters.ShouldSayFromYou(speaker?.Name.TextValue))
        {
            return;
        }

        // Cancel TTS if it's currently BattleTalk addon text, if configured
        if (this.config.CancelSpeechOnTextAdvance &&
            this.backendManager.GetCurrentlySpokenTextSource() == TextSource.TalkAddon)
        {
            this.backendManager.CancelSay(TextSource.TalkAddon);
        }

        Say?.Invoke(speaker, text, TextSource.TalkAddon);
    }
}