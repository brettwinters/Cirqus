﻿using System;
using System.Collections.Generic;
using System.Linq;
using d60.EventSorcerer.Aggregates;
using d60.EventSorcerer.Commands;
using d60.EventSorcerer.Config;
using d60.EventSorcerer.Events;
using d60.EventSorcerer.Tests.Stubs;
using NUnit.Framework;

namespace d60.EventSorcerer.Tests.Integration
{
    [TestFixture, Description(@"Simulates the entire pipeline of event processing:

1. command comes in
2. command is mapped to an operation on an aggregate root
3. new unit of work is created
4. operation is invoked, events are collected
5. part of executing the operation consists of loading another aggregate root
    and calling an operation on that
5. collected events are submitted atomically to event store
    a) on duplicate key error: go back to (3)
6. await synchronous views
7. publish events for consumption by chaser views
")]
    public class SimpleScenarioWithDelegation : FixtureBase
    {
        EventSorcererConfig _eventSorcerer;
        InMemoryAggregateRootRepository _aggregateRootRepository;

        protected override void DoSetUp()
        {
            var eventStore = new InMemoryEventStore();
            
            _aggregateRootRepository = new InMemoryAggregateRootRepository();
            
            var sequenceNumberGenerator = new TestSequenceNumberGenerator(1);
            var commandMapper = new CommandMapper()
                .Map<BearSomeChildrenCommand, ProgrammerAggregate>((c, a) => a.BearChildren(c.IdsOfChildren));

            var viewManager = new ConsoleOutViewManager();

            _eventSorcerer = new EventSorcererConfig(eventStore, _aggregateRootRepository, commandMapper, sequenceNumberGenerator, viewManager);
        }

        [Test]
        public void RunEntirePipelineAndProbePrivatesForMultipleAggregates()
        {
            var firstAggregateRootId = Guid.NewGuid();
            
            var firstChildId = Guid.NewGuid();
            var secondChildId = Guid.NewGuid();
            
            var grandChildId = Guid.NewGuid();

            var initialState = _aggregateRootRepository.ToList();

            _eventSorcerer.ProcessCommand(new BearSomeChildrenCommand(firstAggregateRootId, new[] {firstChildId, secondChildId}));

            var afterBearingTwoChildren = _aggregateRootRepository.ToList();

            _eventSorcerer.ProcessCommand(new BearSomeChildrenCommand(firstChildId, new[] { grandChildId }));

            var afterBearingGrandChild = _aggregateRootRepository.ToList();

            Assert.That(initialState.Count, Is.EqualTo(0), "No events yet, expected there to be zero agg roots in the repo");
            
            Assert.That(afterBearingTwoChildren.Count, Is.EqualTo(3));
            
            var idsOfChildren = afterBearingTwoChildren
                .OfType<ProgrammerAggregate>()
                .Single(p => p.Id == firstAggregateRootId)
                .GetIdsOfChildren();

            Assert.That(idsOfChildren, Is.EqualTo(new[]{firstChildId, secondChildId}));
            
            Assert.That(afterBearingGrandChild.Count, Is.EqualTo(4));

            var idsOfGrandChildren = afterBearingGrandChild
                .OfType<ProgrammerAggregate>()
                .Single(p => p.Id == firstChildId)
                .GetIdsOfChildren();

            Assert.That(idsOfGrandChildren, Is.EqualTo(new[] { grandChildId }));
        }

        public class BearSomeChildrenCommand : Command<ProgrammerAggregate>
        {
            public Guid[] IdsOfChildren { get; private set; }

            public BearSomeChildrenCommand(Guid aggregateRootId, params Guid[] idsOfChildren)
                : base(aggregateRootId)
            {
                IdsOfChildren = idsOfChildren;
            }
        }

        public class ProgrammerAggregate : AggregateRoot
        {
            readonly List<Guid> _idsOfChildren = new List<Guid>();

            public void BearChildren(IEnumerable<Guid> idsOfChildren)
            {
                foreach (var id in idsOfChildren)
                {
                    Load<ProgrammerAggregate>(id).GiveBirth();
                }

                Emit(new HadChildren(idsOfChildren));
            }

            public void Apply(WasBorn e)
            {
                
            }

            public void Apply(HadChildren e)
            {
                _idsOfChildren.AddRange(e.ChildrenIds);
            }

            void GiveBirth()
            {
                Emit(new WasBorn());
            }

            public IEnumerable<Guid> GetIdsOfChildren()
            {
                return _idsOfChildren;
            }
        }

        public class HadChildren : DomainEvent<ProgrammerAggregate>
        {
            public HadChildren(IEnumerable<Guid> childrenIds)
            {
                ChildrenIds = childrenIds.ToArray();
            }

            public Guid[] ChildrenIds { get; private set; }
        }

        public class WasBorn : DomainEvent<ProgrammerAggregate>
        {
            
        }
    }
}