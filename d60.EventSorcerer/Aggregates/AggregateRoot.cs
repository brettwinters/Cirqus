﻿using System;
using d60.EventSorcerer.Events;
using d60.EventSorcerer.Extensions;
using d60.EventSorcerer.Numbers;

namespace d60.EventSorcerer.Aggregates
{
    public abstract class AggregateRoot
    {
        internal IEventCollector EventCollector { get; set; }
        internal ISequenceNumberGenerator SequenceNumberGenerator { get; set; }
        internal IAggregateRootRepository AggregateRootRepository { get; set; }

        public Guid Id { get; private set; }

        internal void Initialize(Guid id, IAggregateRootRepository aggregateRootRepository)
        {
            Id = id;
            AggregateRootRepository = aggregateRootRepository;
        }

        protected void Emit<TAggregateRoot>(DomainEvent<TAggregateRoot> e) where TAggregateRoot : AggregateRoot
        {
            if (e == null) throw new ArgumentNullException("e", "Can't emit null!");

            if (Id == Guid.Empty)
            {
                throw new InvalidOperationException(
                    string.Format(
                        "Attempted to emit event {0} from aggregate root {1}, but it has not yet been assigned an ID!",
                        e, GetType()));
            }

            var eventType = e.GetType();

            if (EventCollector == null)
            {
                throw new InvalidOperationException(string.Format("Attempted to emit event {0}, but the aggreate root does not have an event collector!", e));
            }

            if (SequenceNumberGenerator == null)
            {
                throw new InvalidOperationException(string.Format("Attempted to emit event {0}, but the aggreate root does not have a sequence number generator!", e));
            }

            if (typeof(TAggregateRoot) != GetType())
            {
                throw new InvalidOperationException(
                    string.Format("Attempted to emit event {0} which is owned by {1} from aggregate root of type {2}",
                        e, typeof(TAggregateRoot), GetType()));
            }

            var now = Time.GetUtcNow();
            var sequenceNumber = SequenceNumberGenerator.Next(Id);

            e.Meta[DomainEvent.MetadataKeys.AggregateRootId] = Id;
            e.Meta[DomainEvent.MetadataKeys.TimeLocal] = now.ToLocalTime();
            e.Meta[DomainEvent.MetadataKeys.TimeUtc] = now;
            e.Meta[DomainEvent.MetadataKeys.SequenceNumber] = sequenceNumber;
            e.Meta[DomainEvent.MetadataKeys.Owner] = GetType().Name;
            e.Meta[DomainEvent.MetadataKeys.Version] = eventType.GetFromAttributeOrDefault<VersionAttribute, int>(a => a.Number, 1);

            try
            {
                var dynamicThis = (dynamic)this;

                dynamicThis.Apply((dynamic)e);
            }
            catch (Exception exception)
            {
                throw new ApplicationException(string.Format(@"Could not apply event {0} to {1} - please make sure that the aggregate root type is public and contains an application method with the following signature:

public void Apply({2} e)
{{
    // change aggregate root state in here
}}

", e, this, eventType.Name), exception);
            }

            EventCollector.Add(e);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", GetType().Name, Id);
        }

        protected TAggregateRoot Load<TAggregateRoot>(Guid id) where TAggregateRoot : AggregateRoot, new()
        {
            var aggregateRoot = AggregateRootRepository.Get<TAggregateRoot>(id);
            aggregateRoot.EventCollector = EventCollector;
            aggregateRoot.SequenceNumberGenerator = SequenceNumberGenerator;
            return aggregateRoot;
        }
    }
}