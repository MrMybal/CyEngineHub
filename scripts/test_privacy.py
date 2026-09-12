"""Synthetic regression fixtures only: no removed credential or personal data."""
import importlib.util
import pathlib
import unittest

def load(filename):
    spec = importlib.util.spec_from_file_location(filename, pathlib.Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

repository = load('check-repository-privacy.py')
release = load('check-release-privacy.py')

class PrivacyTests(unittest.TestCase):
    def test_removed_family_ascii_and_utf16(self):
        synthetic = 'a417' + '0' * 24 + '3161'
        for encoding in ('ascii', 'utf-16-le', 'utf-16-be'):
            self.assertIn('removed Bridge credential', repository.inspect('sample.dll', b'\0' * 9000 + synthetic.encode(encoding)))

    def test_configuration_name_without_value_is_allowed(self):
        self.assertEqual([], repository.inspect('sample.cs', b'Environment.GetEnvironmentVariable("CYENGINEHUB_QUIXEL_ALGOLIA_KEY")'))

    def test_private_paths_in_symbols(self):
        sample = 'C:/' + 'Users/' + 'SyntheticFixture/' + 'sample.cs'
        self.assertIn('Windows user profile path', repository.inspect('sample.pdb', sample.encode('utf-16-le')))

    def test_release_refuses_runtime_files_and_symbols(self):
        for name in ('queue-v1.json', 'Cookies', 'test.pdb', 'account.db', 'session.json', '.git/config'):
            self.assertTrue(release.inspect(name, b'{}'), name)

    def test_release_allows_public_manifests(self):
        self.assertEqual([], release.inspect('Extensions/example/extension.json', b'{"id":"example"}'))

if __name__ == '__main__':
    unittest.main()
