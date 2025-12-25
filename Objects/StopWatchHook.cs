using System;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace OBSPlugin.Objects
{
    // This class is from https://github.com/xorus/EngageTimer/blob/7a3d6eb/StopWatchHook.cs
    public class StopWatchHook : IDisposable
    {
        private readonly CombatState _state;
        private readonly ISigScanner _sig;
        private readonly ICondition _condition;
        private readonly IGameInteropProvider _gameInteropProvider;

        private DateTime _combatTimeEnd;

        private DateTime _combatTimeStart;

        private ulong _countDown;
        private IntPtr _countdownPtr;

#pragma warning disable CS0169 // Fields reserved for future implementation
        private bool _countDownRunning;

        /// <summary>
        ///     Ticks since the timer stalled
        /// </summary>
        private int _countDownStallTicks;

        private float _lastCountDownValue;
#pragma warning restore CS0169

        private readonly CountdownTimer _countdownTimer;
        private Hook<CountdownTimer>? _countdownTimerHook;
        private bool _shouldRestartCombatTimer = true;
        private bool _useAgentFallback;

        public StopWatchHook(
            CombatState state,
            ISigScanner sig,
            ICondition condition,
            IGameInteropProvider gameInteropProvider
        )
        {
            _state = state;
            _sig = sig;
            _condition = condition;
            _gameInteropProvider = gameInteropProvider;
            _countDown = 0;
            _countdownTimer = CountdownTimerFunc;
            HookCountdownPointer();
        }

        public void Dispose()
        {
            if (_countdownTimerHook == null) return;
            _countdownTimerHook.Disable();
            _countdownTimerHook.Dispose();
        }

        private IntPtr CountdownTimerFunc(ulong value)
        {
            _countDown = value;
            return _countdownTimerHook.Original(value);
        }

        public void Update()
        {
            if (_state.Mocked) return;
            UpdateCountDown();
            UpdateEncounterTimer();
            _state.InInstance = _condition[ConditionFlag.BoundByDuty];
        }

        private void HookCountdownPointer()
        {
            try
            {
                // Check if SigScanner service was injected
                if (_sig == null)
                {
                    Plugin.PluginLog.Warning("SigScanner service not available - using FFXIVClientStructs Agent fallback");
                    _useAgentFallback = true;
                    return;
                }

                // Signature from EngageTimer/DelvUI - valid for FFXIV 7.x
                if (!_sig.TryScanText("40 53 48 83 EC 40 80 79 38 00", out _countdownPtr))
                {
                    Plugin.PluginLog.Warning("Could not find countdown timer signature - using FFXIVClientStructs Agent fallback");
                    _useAgentFallback = true;
                    return;
                }

                _countdownTimerHook = _gameInteropProvider.HookFromAddress<CountdownTimer>(_countdownPtr, _countdownTimer);
                _countdownTimerHook.Enable();
                Plugin.PluginLog.Info("Countdown timer hook installed successfully");
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error("Could not hook to timer, using Agent fallback\n" + e);
                _useAgentFallback = true;
            }
        }

        private void UpdateEncounterTimer()
        {
            if (_condition[ConditionFlag.InCombat])
            {
                _state.InCombat = true;
                if (_shouldRestartCombatTimer)
                {
                    _shouldRestartCombatTimer = false;
                    _combatTimeStart = DateTime.Now;
                }

                _combatTimeEnd = DateTime.Now;
            }
            else
            {
                _state.InCombat = false;
                _shouldRestartCombatTimer = true;
            }

            _state.CombatStart = _combatTimeStart;
            _state.CombatDuration = _combatTimeEnd - _combatTimeStart;
            _state.CombatEnd = _combatTimeEnd;
        }

        private void UpdateCountDown()
        {

            if (_useAgentFallback)
            {
                UpdateCountDownFromAgent();
                return;
            }

            if (_countDown == 0)
            {
                _state.CountingDown = false;
                _state.CountDownValue = 0f;
                return;
            }

            // Offsets from FFXIVClientStructs AgentCountDownSettingDialog:
            // 0x28 = TimeRemaining (float)
            // 0x38 = Active (bool)
            var countDownActive = Marshal.PtrToStructure<byte>((IntPtr)_countDown + 0x38) == 1;
            var countDownValue = Marshal.PtrToStructure<float>((IntPtr)_countDown + 0x28);
            _state.CountingDown = countDownActive && countDownValue > 0f;
            _state.CountDownValue = countDownValue;
        }

        private unsafe void UpdateCountDownFromAgent()
        {
            try
            {
                var agentModule = AgentModule.Instance();
                if (agentModule == null)
                {
                    _state.CountingDown = false;
                    _state.CountDownValue = 0f;
                    return;
                }

                var agent = agentModule->GetAgentByInternalId(AgentId.CountDownSettingDialog);
                if (agent == null)
                {
                    _state.CountingDown = false;
                    _state.CountDownValue = 0f;
                    return;
                }

                var countdownAgent = (AgentCountDownSettingDialog*)agent;
                var isActive = countdownAgent->Active;
                var timeRemaining = countdownAgent->TimeRemaining;

                _state.CountingDown = isActive && timeRemaining > 0f;
                _state.CountDownValue = timeRemaining;
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error("Error reading countdown from Agent: " + e.Message);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Ansi)]
        private delegate IntPtr CountdownTimer(ulong p1);
    }
}
