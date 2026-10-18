/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.CSharp.Strategies
{
    /// <summary>
    /// Monthly rotation into the single best performer of a universe over a lookback window, or cash when
    /// nothing is up. Exits on the switch day and enters the next day so leveraged positions never need
    /// more buying power than the account has.
    /// </summary>
    public abstract class RotationStrategyBase : PopularStrategyBase
    {
        private readonly Dictionary<Symbol, RateOfChange> _momentum = new();
        private int _lastMonth = -1;
        private Symbol _pendingTarget;

        protected abstract string[] Tickers { get; }
        protected virtual int Lookback => 252;
        protected virtual decimal Exposure => 0.98m;
        /// <summary>
        /// Maximum leverage the account allows. Above 2x this models portfolio margin or futures, not a Reg T account.
        /// </summary>
        protected virtual decimal AccountLeverage => 2m;

        public override void Initialize()
        {
            base.Initialize();
            foreach (var ticker in Tickers)
            {
                var symbol = ticker == "SPY" ? Spy : AddEquity(ticker, Resolution.Daily).Symbol;
                Securities[symbol].SetLeverage(AccountLeverage);
                _momentum[symbol] = ROC(symbol, Lookback, Resolution.Daily);
            }
            SetWarmUp(Lookback, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp) return;

            if (_pendingTarget != null)
            {
                SetHoldings(_pendingTarget, Exposure);
                _pendingTarget = null;
                return;
            }

            if (_momentum.Values.Any(x => !x.IsReady) || Time.Month == _lastMonth) return;
            _lastMonth = Time.Month;

            var best = _momentum.OrderByDescending(x => x.Value.Current.Value).First();
            var target = best.Value > 0 ? best.Key : null;

            var exits = Portfolio.Values.Where(x => x.Invested && x.Symbol != target).ToList();
            foreach (var holding in exits)
            {
                Liquidate(holding.Symbol);
            }
            if (target == null || Portfolio[target].Invested) return;

            if (exits.Count > 0)
            {
                _pendingTarget = target;
            }
            else
            {
                SetHoldings(target, Exposure);
            }
        }
    }

    /// <summary>
    /// The ETF momentum rotation (SPY, QQQ, IWM, EEM, 12-month lookback) run at 2x leverage
    /// </summary>
    public class LeveragedMomentumAlgorithm : RotationStrategyBase
    {
        protected override string[] Tickers => new[] { "SPY", "QQQ", "IWM", "EEM" };
        protected override decimal Exposure => 1.9m;
    }

    /// <summary>
    /// The ETF momentum rotation at 3x, in an account that allows up to 5x
    /// </summary>
    public class TripleMomentumAlgorithm : RotationStrategyBase
    {
        protected override string[] Tickers => new[] { "SPY", "QQQ", "IWM", "EEM" };
        protected override decimal Exposure => 3m;
        protected override decimal AccountLeverage => 5m;
    }

    /// <summary>
    /// The ETF momentum rotation at 4x, in an account that allows up to 6x
    /// </summary>
    public class QuadMomentumAlgorithm : RotationStrategyBase
    {
        protected override string[] Tickers => new[] { "SPY", "QQQ", "IWM", "EEM" };
        protected override decimal Exposure => 4m;
        protected override decimal AccountLeverage => 6m;
    }

    /// <summary>
    /// ETF momentum rotation with a 3-month lookback, so it reacts faster to new leaders
    /// </summary>
    public class FastMomentumAlgorithm : RotationStrategyBase
    {
        protected override string[] Tickers => new[] { "SPY", "QQQ", "IWM", "EEM" };
        protected override int Lookback => 63;
    }

    /// <summary>
    /// Single-stock momentum: each month hold the one stock with the best 6-month return
    /// </summary>
    public class StockMomentumAlgorithm : RotationStrategyBase
    {
        protected override string[] Tickers => new[] { "AAPL", "GOOG", "IBM", "BAC", "AIG", "WM" };
        protected override int Lookback => 126;
    }

    /// <summary>
    /// 2x QQQ while QQQ is above its 200-day SMA, otherwise cash
    /// </summary>
    public class LeveragedQqqTrendAlgorithm : PopularStrategyBase
    {
        private Symbol _qqq;
        private SimpleMovingAverage _sma;

        protected virtual decimal Exposure => 1.9m;
        protected virtual decimal AccountLeverage => 2m;

        public override void Initialize()
        {
            base.Initialize();
            _qqq = AddEquity("QQQ", Resolution.Daily, leverage: AccountLeverage).Symbol;
            _sma = SMA(_qqq, 200, Resolution.Daily);
            SetWarmUp(200, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_sma.IsReady) return;

            var price = Securities[_qqq].Price;
            if (price > _sma && !Portfolio.Invested)
            {
                SetHoldings(_qqq, Exposure);
            }
            else if (price < _sma && Portfolio.Invested)
            {
                Liquidate(_qqq);
            }
        }
    }

    /// <summary>
    /// 3x QQQ while QQQ is above its 200-day SMA (roughly TQQQ with a trend filter), in an account that allows up to 5x
    /// </summary>
    public class TripleQqqTrendAlgorithm : LeveragedQqqTrendAlgorithm
    {
        protected override decimal Exposure => 3m;
        protected override decimal AccountLeverage => 5m;
    }

    /// <summary>
    /// Long SPY above its 200-day SMA, short SPY below it, always in the market
    /// </summary>
    public class LongShortTrendAlgorithm : PopularStrategyBase
    {
        private SimpleMovingAverage _sma;

        public override void Initialize()
        {
            base.Initialize();
            _sma = SMA(Spy, 200, Resolution.Daily);
            SetWarmUp(200, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_sma.IsReady) return;

            var price = Securities[Spy].Price;
            var quantity = Portfolio[Spy].Quantity;
            if (price > _sma && quantity <= 0)
            {
                SetHoldings(Spy, 1);
            }
            else if (price < _sma && quantity >= 0)
            {
                SetHoldings(Spy, -1);
            }
        }
    }
}
