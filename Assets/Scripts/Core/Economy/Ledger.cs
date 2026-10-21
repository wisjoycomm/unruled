using System;
using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// Owns <see cref="Wallet"/>: the only code that changes a balance. Every move is all-or-nothing, no balance goes
    /// negative. <see cref="Transfer"/> moves money, <see cref="Burn"/> removes it (upkeep sinks, repaid loans). Money
    /// is created only by <see cref="Grant"/>, for world setup, and <see cref="Lend"/>, THE SYSTEM's loans.
    /// </summary>
    public sealed class Ledger
    {
        private readonly ComponentStore<Wallet> _wallets;
        private EntityId[] _byPlayer = new EntityId[16];

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

        /// <summary>A loan from THE SYSTEM (see <see cref="LoanSystem"/>): adds credits the borrower now owes.</summary>
        public void Lend(EntityId wallet, long cents) => Grant(wallet, cents);

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
                if (playerId > 0 && playerId < _byPlayer.Length)
                {
                    var cached = _byPlayer[playerId];
                    if (!cached.IsNull && _wallets.Has(cached) && _wallets.Get(cached).OwnerPlayerId == playerId)
                    {
                        wallet = cached;
                        return true;
                    }
                }
                for (int i = 0; i < _wallets.Count; i++)
                {
                    if (_wallets.At(i).OwnerPlayerId == playerId)
                    {
                        wallet = _wallets.EntityAt(i);
                        if (playerId > 0)
                        {
                            if (playerId >= _byPlayer.Length)
                                Array.Resize(ref _byPlayer, Math.Max(playerId + 1, _byPlayer.Length * 2));
                            _byPlayer[playerId] = wallet;
                        }
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
