import math
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Tools' / 'CadRevision'))
from cloud_review import chain, relation, sample


class CloudReviewTests(unittest.TestCase):
    polygon = [(0, 0), (100, 0), (100, 100), (0, 100), (0, 0)]

    def test_inside_outside_and_crossing(self):
        self.assertEqual(relation([[20, 20, 80, 80]], self.polygon), '圈內')
        self.assertEqual(relation([[-20, 20, -10, 80]], self.polygon), '僅矩形相交')
        self.assertEqual(relation([[-20, 50, 120, 50]], self.polygon), '邊界待查')

    def test_tangent_and_near_boundary(self):
        self.assertEqual(relation([[-10, 0, 110, 0]], self.polygon), '邊界待查')
        self.assertEqual(relation([[2, 20, 2, 80]], self.polygon), '邊界待查')

    def test_moved_versions(self):
        self.assertEqual(relation([[20, 20, 80, 20], [120, 20, 180, 20]], self.polygon), '跨版本內外')

    def test_circle(self):
        self.assertEqual(relation([[50, 50, 10, 0, math.tau, True]], self.polygon), '圈內')
        self.assertEqual(relation([[50, 50, 50, 0, math.tau, True]], self.polygon), '邊界待查')

    def test_chain_order_and_failure(self):
        result = chain([[100, 100, 0, 100], [0, 0, 100, 0], [100, 0, 100, 100], [0, 0, 0, 100]])
        self.assertEqual(result[0], result[-1])
        with self.assertRaises(ValueError):
            chain([[0, 0, 10, 0], [100, 0, 110, 0]])

    def test_sagitta(self):
        points = sample([0, 0, 10000, 0, math.pi, False])
        for a, b in zip(points, points[1:]):
            self.assertLessEqual(10000-math.hypot((a[0]+b[0])/2,(a[1]+b[1])/2), 1.000001)


if __name__ == '__main__':
    unittest.main()
