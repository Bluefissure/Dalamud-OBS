using System;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

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
        private bool _countDownRunning;

        /// <summary>
        ///     Ticks since the timer stalled
        /// </summary>
        private int _countDownStallTicks;

        private readonly CountdownTimer _countdownTimer;
        private Hook<CountdownTimer> _countdownTimerHook;
        private float _lastCountDownValue;
        private bool _shouldRestartCombatTimer = true;

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
            _countdownPtr = _sig.ScanText("40 53 48 83 EC 40 80 79 38 00");
            try
            {
                //_countdownTimerHook = new Hook<CountdownTimer>(_countdownPtr, _countdownTimer);
                _countdownTimerHook = _gameInteropProvider.HookFromAddress<CountdownTimer>(_countdownPtr, _countdownTimer);
                _countdownTimerHook.Enable();
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Error("Could not hook to timer\n" + e);
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
            _state.CountingDown = false;
            if (_countDown == 0) return;
            var countDownActive = Marshal.PtrToStructure<byte>((IntPtr)_countDown + 0x38) == 1;
            var countDownPointerValue = Marshal.PtrToStructure<float>((IntPtr)_countDown + 0x2c);
            _state.CountingDown = countDownActive && countDownPointerValue > 0f;
            _state.CountDownValue = countDownPointerValue;
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Ansi)]
        private delegate IntPtr CountdownTimer(ulong p1);
    }
}
