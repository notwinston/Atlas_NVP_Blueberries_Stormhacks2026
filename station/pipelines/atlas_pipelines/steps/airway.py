"""Generate the model airway tree (tree.json) and the greedy compass study (greedy.json)."""

from __future__ import annotations

import sys

from .. import airway, cache, contract, paths
from ..cli import StepError

NAME = "airway"
HELP = "model-generated airway: airway/tree.json (atlas.tree/2) and airway/greedy.json (atlas.greedy/1)"
CODE_VERSION = "airway/1"


def add_arguments(parser) -> None:
    parser.add_argument("--seed", type=int, default=airway.DEFAULT_SEED,
                        help=f"seed of the tree, recorded in tree.json (default {airway.DEFAULT_SEED})")
    parser.add_argument("--greedy-seed", type=int, default=None,
                        help="seed of the 1,000 study targets, recorded in greedy.json (default: the tree seed)")
    parser.add_argument("--materialised-gen", type=int, default=airway.MATERIALISED_GEN,
                        help=f"deepest generation written to tree.json (default {airway.MATERIALISED_GEN})")
    parser.add_argument("--target-gen", type=int, default=airway.ROUTE_FORKS,
                        help="generation of the target (default 4: the four-fork route ends at the target; "
                             "a larger value puts the target deeper than the end of the four-fork route)")
    parser.add_argument("--strict", action="store_true",
                        help="exit with an error when no target gives exactly one Two Compasses fork")


def run(args) -> int:
    if not 0 <= args.materialised_gen <= 14:
        raise StepError("--materialised-gen must be 0..14")
    if not airway.ROUTE_FORKS <= args.target_gen <= airway.MAX_ADDRESS_LEN:
        raise StepError(f"--target-gen must be {airway.ROUTE_FORKS}..{airway.MAX_ADDRESS_LEN}")
    greedy_seed = args.seed if args.greedy_seed is None else args.greedy_seed
    out_dir = paths.case_dir(args.case, args.out, create=True) / "airway"
    tree_path = out_dir / "tree.json"
    greedy_path = out_dir / "greedy.json"

    def work() -> None:
        out_dir.mkdir(parents=True, exist_ok=True)
        contract.write_json(tree_path, airway.build_tree(args.seed, args.materialised_gen, args.target_gen))
        contract.write_json(greedy_path, airway.greedy_study(args.seed, greedy_seed))

    ran = cache.run_cached(
        stamp=paths.stamp_path(args.case, NAME, args.out),
        code_version=CODE_VERSION,
        params={"seed": args.seed, "greedy_seed": greedy_seed, "materialised_gen": args.materialised_gen,
                "target_gen": args.target_gen, "rule": airway.RULE, "greedy_rule": airway.GREEDY_RULE},
        inputs=[],
        outputs=[tree_path, greedy_path],
        work=work,
        force=args.force,
    )
    tree = contract.read_json(tree_path)
    greedy = contract.read_json(greedy_path)
    print(f"airway: {'built' if ran else 'up to date (cached)'} {out_dir}")
    print(f"  tree    seed {tree['seed']}, {len(tree['nodes'])} nodes written (generations 0..{tree['materialised_gen']}), "
          f"addresses resolvable to length {tree['max_address_len']}")
    print(f"  target  {tree['target']['address']!r}, Two Compasses fork {tree['compass_fork']['address']!r}")
    print(f"  greedy  {greedy['wrong_forks']} of {greedy['forks']} forks wrong "
          f"(share {greedy['wrong_share']:.6f}) over {greedy['n_targets']} targets, seed {greedy['seed']}")
    if tree["compass_fork"]["address"] is None:
        msg = (f"airway: NO Two Compasses fork: no generation-{args.target_gen} target has exactly one fork among "
               "the four forks of its route at which the Euclidean compass is wrong; compass_fork.address is null")
        if args.strict:
            raise StepError(msg)
        print("WARNING " + msg, file=sys.stderr)
    return 0
