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

using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Orders.Fees;

namespace QuantConnect.Algorithm.CSharp.Strategies
{
    /// <summary>
    /// Shared setup: daily Coinbase BTCUSD, Jul 2015 to Aug 2018 (the bundled daily data runs Dec 2014 to Aug 2018,
    /// the first months feed the 200-day warm up). Unleveraged strategies use a Coinbase cash account; leveraged
    /// ones use a margin account with Coinbase fees.
    /// </summary>
    public abstract class CryptoStrategyBase : QCAlgorithm
    {
        protected Symbol Btc;

        protected virtual decimal Exposure => 0.98m;

        public override void Initialize()
        {
            SetStartDate(2015, 7, 1);
            SetEndDate(2018, 8, 13);
            SetCash(100000);

            if (Exposure <= 1)
            {
                SetBrokerageModel(BrokerageName.Coinbase, AccountType.Cash);
            }
            var btc = AddCrypto("BTCUSD", Resolution.Daily, Market.Coinbase);
            if (Exposure > 1)
            {
                btc.SetFeeModel(new CoinbaseFeeModel());
                btc.SetLeverage(Exposure + 2);
            }
            Btc = btc.Symbol;
            SetBenchmark(Btc);
        }
    }

    /// <summary>
    /// Benchmark: buy BTC on day one and hold
    /// </summary>
    public class BuyAndHoldBtcAlgorithm : CryptoStrategyBase
    {
        public override void OnData(Slice slice)
        {
            if (!Portfolio.Invested)
            {
                SetHoldings(Btc, Exposure);
            }
        }
    }

    /// <summary>
    /// Golden cross on BTC: hold while the 50-day SMA is above the 200-day SMA, otherwise cash
    /// </summary>
    public class BtcGoldenCrossAlgorithm : CryptoStrategyBase
    {
        private SimpleMovingAverage _fast;
        private SimpleMovingAverage _slow;

        public override void Initialize()
        {
            base.Initialize();
            _fast = SMA(Btc, 50, Resolution.Daily);
            _slow = SMA(Btc, 200, Resolution.Daily);
            SetWarmUp(200, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_slow.IsReady) return;

            if (_fast > _slow && !Portfolio.Invested)
            {
                SetHoldings(Btc, Exposure);
            }
            else if (_fast < _slow && Portfolio.Invested)
            {
                Liquidate(Btc);
            }
        }
    }

    /// <summary>
    /// Trend filter on BTC: hold while BTC closes above its 50-day SMA, otherwise cash
    /// </summary>
    public class BtcTrendAlgorithm : CryptoStrategyBase
    {
        private SimpleMovingAverage _sma;

        public override void Initialize()
        {
            base.Initialize();
            _sma = SMA(Btc, 50, Resolution.Daily);
            SetWarmUp(50, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_sma.IsReady) return;

            var price = Securities[Btc].Price;
            if (price > _sma && !Portfolio.Invested)
            {
                SetHoldings(Btc, Exposure);
            }
            else if (price < _sma && Portfolio.Invested)
            {
                Liquidate(Btc);
            }
        }
    }

    /// <summary>
    /// The BTC 50-day trend filter at 2x
    /// </summary>
    public class BtcTrend2xAlgorithm : BtcTrendAlgorithm
    {
        protected override decimal Exposure => 2m;
    }

    /// <summary>
    /// The BTC 50-day trend filter at 3x
    /// </summary>
    public class BtcTrend3xAlgorithm : BtcTrendAlgorithm
    {
        protected override decimal Exposure => 3m;
    }

    /// <summary>
    /// RSI dip buying on BTC: buy when RSI(14) drops below 30, sell when it rises above 70
    /// </summary>
    public class BtcRsiDipAlgorithm : CryptoStrategyBase
    {
        private RelativeStrengthIndex _rsi;

        public override void Initialize()
        {
            base.Initialize();
            _rsi = RSI(Btc, 14, MovingAverageType.Wilders, Resolution.Daily);
            SetWarmUp(30, Resolution.Daily);
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp || !_rsi.IsReady) return;

            if (!Portfolio.Invested && _rsi < 30)
            {
                SetHoldings(Btc, Exposure);
            }
            else if (Portfolio.Invested && _rsi > 70)
            {
                Liquidate(Btc);
            }
        }
    }
}
