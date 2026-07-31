using System;
using System.Collections.Generic;
using System.Linq;
using BobCoach.Engine;

namespace BobCoach.Engine.Decision
{
    public sealed class CandidateGenerator
    {
        private readonly ActionEnumerator _enumerator;

        public CandidateGenerator(ActionEnumerator enumerator)
        {
            _enumerator = enumerator;
        }

        public IList<ActionDescriptor> Generate(GameState state)
        {
            if (state == null || !state.GameActive) return new List<ActionDescriptor>();

            return _enumerator.Enumerate(state, state.HeroCardId)
                .Select(action => ActionDescriptor.From(state, action))
                .OrderBy(candidate => candidate.ActionKey, StringComparer.Ordinal)
                .ToList();
        }
    }
}
