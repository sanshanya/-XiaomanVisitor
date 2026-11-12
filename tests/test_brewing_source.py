"""Every material rejection state must have a usable localized tablet message."""
import csv
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class BrewingSourceTests(unittest.TestCase):
    def test_collection_states_and_item_names_are_localized(self):
        source = (ROOT/'src/Domain/BrewingProgress.cs').read_text(encoding='utf-8-sig')
        states = re.search(r'enum BrewingCollectionState\s*\{([^}]+)\}', source)[1]
        keys = {'ui_brew_state_'+state.strip().lower() for state in states.split(',') if state.strip() != 'Ready'}
        keys.add('ui_brew_invalid_item')
        keys.add('ui_brew_state_visitended')
        authored = json.loads((ROOT/'assets/brewing-localization.json').read_text(encoding='utf-8'))
        for lang in ('zh','ja','en'):
            with (ROOT/f'package/XiaomanVisitor/Localization/{lang}.csv').open(encoding='utf-8', newline='') as handle:
                actual = dict(csv.reader(handle))
            for key in keys:
                self.assertTrue(authored[lang][key].strip(), (lang,key))
                self.assertEqual(actual[key], authored[lang][key], (lang,key))
                self.assertEqual(re.findall(r'\{\d+\}', authored['zh'][key]), re.findall(r'\{\d+\}', actual[key]), (lang,key))
