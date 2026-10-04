# Maths card

## Network Atlas

Source: `documents/network_atlas/ATLAS_Hyperbolic_Network_Design.md` section 3 (model of Krioukov et al. 2010). All formulas use ζ = 1: one unit of hyperbolic distance is one curvature radius, which ATLAS maps to L metres. Every row below names the tests that prove it, by full name (`docs/contracts/network.md` section 8).

| Quantity | Formula (section 3) | Source | Proved by |
|---|---|---|---|
| Exponent of the radial density | α = (γ − 1) / 2 | eq. 29 with ζ = 1 | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| Helper | ξ = α / (α − 1/2) | eq. 25 | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| Scale | ν = π k̄ / (2 ξ²) at T = 0; ν = k̄ sin(πT) / (2 T ξ²) for 0 < T < 1 | eqs. 25, 31; hot case of §VII | `station/pipelines/tests/test_network.py::test_alpha_xi_nu_eqs_25_29_31` |
| Disc radius | R = 2 ln(N / ν); for γ = 3 this is R = 2 ln(8N / (π k̄)) | eq. 13 | `station/pipelines/tests/test_network.py::test_radius_eq13` |
| Radius of a node | r = arccosh(1 + u (cosh αR − 1)) / α, with u uniform in [0, 1] | exact inverse of the CDF of eq. 17 | `station/pipelines/tests/test_network.py::test_node_radius_inverts_cdf_eq17` |
| Angle | θ uniform in [0, 2π) |  | `station/pipelines/tests/test_network.py::test_angle_uniform_in_0_2pi` |
| Distance | cosh x = cosh r cosh r′ − sinh r sinh r′ cos Δθ, with Δθ = π − abs(π − abs(θ − θ′)) | eq. 5 | `station/pipelines/tests/test_network.py::test_distance_eq5_equals_golden`, `Atlas.Blades.Network.Tests.NetworkModelTests.Distances_EqualPython_1e9` |
| Link, T = 0 | linked if x < R | eq. 8 | `station/pipelines/tests/test_network.py::test_links_iff_distance_below_R_eq8` |
| Link, T > 0 | p(x) = 1 / (e^((x − R) / (2T)) + 1) | eq. 41 | `station/pipelines/tests/test_network.py::test_hot_link_probability_eq41` |
| Greedy forwarding | next = the neighbour with the smallest x to the target; drop when no neighbour is closer | §X | `station/pipelines/tests/test_network.py::test_greedy_routes_equal_golden`, `Atlas.Blades.Network.Tests.GreedyRouterTests.Routes_EqualPython_1000Pairs_BothRules`, `station/console-tests/network.test.js › network: greedy routes equal golden_routes on 1,000 pairs, both rules` |
| Poincaré disc point | ρ = tanh(r / 2), at angle θ | conformal model | `station/pipelines/tests/test_network.py::test_poincare_point_equals_golden`, `Atlas.Blades.Network.Tests.PoincareMapTests.Project_EqualsPythonPoincare_1e9` |
| Re-centring on point a | z → (z − a) / (1 − ā z) | Möbius isometry of the disc | `station/pipelines/tests/test_network.py::test_mobius_preserves_distance_within_R`, `Atlas.Blades.Network.Tests.PoincareMapTests.Mobius_PreservesPairwiseDistances_1e9` |
| In the ATLAS kernel frame | P = (sinh r cos θ, sinh r sin θ, cosh r), metric diag(1, 1, −1), κ = −1 | lane B contract; `Geometry2.Distance` | `Atlas.Blades.Network.Tests.NetworkModelTests.HyperboloidPoint_DistanceEqualsGeometry2_1e9` |

Default parameters: N = 200, k̄ = 6, γ = 2.5, T = 0. These give R = 10.5 units.

### Claims we make and claims we avoid

We say:

- "This network was *generated* by the hyperbolic model of Krioukov et al. 2010; real networks share its hubs, clustering and navigability."
- "With the hyperbolic rule, greedy delivery succeeded on X of Y random packets in *this* network."
- "Flat-picture greedy fails more often on the same network."
- "Hyperbolic space has room for every branch of a tree."

We do not say:

- "This is the real Internet" (unless a real embedded map is used, and credited)
- "Greedy routing always works" (it is measured, not guaranteed)
- "Euclidean geometry cannot route" (a different flat embedding could do better)
- Anything clinical about the Lung by analogy

Known gap (contract section 9, C1): at N = 200 the generated mean degree is below k̄ (4.59 against 6 over seeds 0 to 9), so the mean-degree test is `xfail(strict=True)`.
