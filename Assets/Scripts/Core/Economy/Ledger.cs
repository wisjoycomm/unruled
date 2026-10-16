using System;
using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Owns <see cref="Wallet"/>: the only code that changes a balance. Every move is all-or-nothing, no balance goes
    /// negative, and money is never created during play: <see cref="Transfer"/> moves it, <see cref="Burn"/> removes it
    /// (upkeep sinks). Only <see cref="Grant"/>, for world setup, adds money.
    /// </summary>
    public sealed class Ledger
    {
        private readonly ComponentStore<Wallet> _wallets;

        public Ledger(ComponentStore<Wallet> wallets)
        {
            _wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
        }

        public long Balance(EntityId wallet) => _wallets.Get(wallet).Cents;

        /// <summary>World setup only: adds credits that did not exist before.</summary>
        public void Grant(EntityId wallet, long cents)
        {
            if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
            ref var w = ref _wallets.Get(wallet);
            w.Cents = checked(w.Cents + cents);
        }

        public bool Transfer(EntityId from, EntityId to, long cents)
        {
            if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
            if (!_wallets.Has(from) || !_wallets.Has(to) || _wallets.Get(from).Cents < cents)
                return false;
            _wallets.Get(from).Cents -= cents;
            ref var target = ref _wallets.Get(to);
            target.Cents = checked(target.Cents + cents);
            return true;
        }

        public bool Burn(EntityId from, long cents)
        {
            if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
            if (!_wallets.Has(from) || _wallets.Get(from).Cents < cents)
                return false;
            _wallets.Get(from).Cents -= cents;
            return true;
        }

        public bool TryFindPlayerWallet(int playerId, out EntityId wallet)
        {
            if (playerId != 0)
            {
                for (int i = 0; i < _wallets.Count; i++)
                {
                    if (_wallets.At(i).OwnerPlayerId == playerId)
                    {
                        wallet = _wallets.EntityAt(i);
                        return true;
                    }
                }
            }
            wallet = default;
            return false;
        }

        public bool TryFindPool(TaxPoolKind pool, out EntityId wallet)
        {
            if (pool != TaxPoolKind.None)
            {
                for (int i = 0; i < _wallets.Count; i++)
                {
                    if (_wallets.At(i).Pool == pool)
                    {
                        wallet = _wallets.EntityAt(i);
                        return true;
                    }
                }
            }
            wallet = default;
            return false;
        }
    }
}
