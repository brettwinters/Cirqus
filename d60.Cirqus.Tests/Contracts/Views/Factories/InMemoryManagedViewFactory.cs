﻿using d60.Cirqus.Views.ViewManagers;

namespace d60.Cirqus.Tests.Contracts.Views.Factories
{
    public class InMemoryManagedViewFactory : AbstractManagedViewFactory
    {
        protected override IViewManager<TViewInstance> CreateManagedView<TViewInstance>()
        {
            return new InMemoryViewManager<TViewInstance>();
        }
    }
}