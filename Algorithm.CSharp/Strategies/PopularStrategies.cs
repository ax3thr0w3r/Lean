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

using System.Linq;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace QuantConnect.Algorithm.CSharp.Strategies
{
    /// <summary>
    /// Shared setup: daily SPY, Jan 2005 to Mar 2021 (the span the bundled daily data covers for every ticker used here)
    /// </summary>
    public abstract class PopularStrategyBase : QCAlgorithm
    {
        protected Symbol Spy;

        public override void Initialize()
        {
            SetStartDate(2005, 1, 1);
            SetEndDate(2021, 3, 31);
            SetCash(100000);
            Spy = AddEquity("SPY", Resolution.Daily).Symbol;
            SetBenchmark(Spy);
        }
    }

    /// <summary>
    /// Benchmark: buy SPY on day one and hold
    /// </summary>
    public class BuyAndHoldSpyAlgorithm : PopularStrategyBase
    {
        public override void OnData(Slice slice)
        {
            if (!Portfolio.Invested)
            {
                SetHoldings(Spy, 1);
            }
        }
    }

    /// <summary>
    /// Golden cross: hold SPY while the 50-day SMA is above the 200-day SMA, otherwise cash
    /// </summary>
    public class GoldenCrossAlgorithm : PopularStrategyBase
    {
        private SimpleMovingAverage _fast;
        private SimpleMovingAverage _slow;

        public override void Initialize()
        {
            base.Initialize();
            _fast = SMA(Spy, 50, Resolution.Daily);
            _slow = SMA(Spy, 200, Resolution.Daily);
            SetWarmUp(200, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_slow.IsReady) return;

            if (_fast > _slow && !Portfolio.Invested)
            {
                SetHoldings(Spy, 1);
            }
            else if (_fast < _slow && Portfolio.Invested)
            {
                Liquidate(Spy);
            }
        }
    }

    /// <summary>
    /// 200-day trend filter (Faber): hold SPY while it closes above its 200-day SMA, otherwise cash
    /// </summary>
    public class TrendFilterAlgorithm : PopularStrategyBase
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
            if (price > _sma && !Portfolio.Invested)
            {
                SetHoldings(Spy, 1);
            }
            else if (price < _sma && Portfolio.Invested)
            {
                Liquidate(Spy);
            }
        }
    }

    /// <summary>
    /// RSI(2) mean reversion (Connors): in an uptrend (above 200-day SMA), buy SPY when RSI(2) drops below 10,
    /// sell once it closes above its 5-day SMA
    /// </summary>
    public class Rsi2MeanReversionAlgorithm : PopularStrategyBase
    {
        private RelativeStrengthIndex _rsi;
        private SimpleMovingAverage _trend;
        private SimpleMovingAverage _exit;

        public override void Initialize()
        {
            base.Initialize();
            _rsi = RSI(Spy, 2, MovingAverageType.Wilders, Resolution.Daily);
            _trend = SMA(Spy, 200, Resolution.Daily);
            _exit = SMA(Spy, 5, Resolution.Daily);
            SetWarmUp(200, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_trend.IsReady) return;

            var price = Securities[Spy].Price;
            if (!Portfolio.Invested && price > _trend && _rsi < 10)
            {
                SetHoldings(Spy, 1);
            }
            else if (Portfolio.Invested && price > _exit)
            {
                Liquidate(Spy);
            }
        }
    }

    /// <summary>
    /// Momentum rotation: each month hold whichever of SPY, QQQ, IWM, EEM has the best 12-month return,
    /// or cash if none of them is up over the year
    /// </summary>
    public class MomentumRotationAlgorithm : PopularStrategyBase
    {
        private readonly string[] _tickers = { "QQQ", "IWM", "EEM" };
        private readonly System.Collections.Generic.Dictionary<Symbol, RateOfChange> _momentum = new();
        private int _lastMonth = -1;

        public override void Initialize()
        {
            base.Initialize();
            _momentum[Spy] = ROC(Spy, 252, Resolution.Daily);
            foreach (var ticker in _tickers)
            {
                var symbol = AddEquity(ticker, Resolution.Daily).Symbol;
                _momentum[symbol] = ROC(symbol, 252, Resolution.Daily);
            }
            SetWarmUp(252, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || _momentum.Values.Any(x => !x.IsReady) || Time.Month == _lastMonth) return;
            _lastMonth = Time.Month;

            var best = _momentum.OrderByDescending(x => x.Value.Current.Value).First();
            var target = best.Value > 0 ? best.Key : null;

            foreach (var holding in Portfolio.Values.Where(x => x.Invested && x.Symbol != target))
            {
                Liquidate(holding.Symbol);
            }
            if (target != null && !Portfolio[target].Invested)
            {
                // Leave a small buffer: the old position is still held when this order is checked for buying power
                SetHoldings(target, 0.98m);
            }
        }
    }
}
