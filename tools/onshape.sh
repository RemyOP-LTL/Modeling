#!/bin/bash
# Talk to Onshape with the keys in ../onshape-keys.env (never printed, never committed).
#
#   onshape.sh api METHOD PATH [json-body-file]   raw API call, PATH starts after /api/v10
#   onshape.sh push FEATURE_STUDIO_ID FILE.fs     replace a Feature Studio's code with a local file
#   onshape.sh view PART_ID OUT.png [VIEW]        render one part (VIEW: isometric, front, top, ...)
#   onshape.sh parts                              list the parts in the Part Studio
#   onshape.sh status                             status of every feature in the Part Studio
#   onshape.sh message FEATURE_ID                 a feature's status and message (the text Onshape shows on it)
#   onshape.sh set FEATURE_ID key=value ...       change a feature's settings, e.g. pentagonDiameter="5 in" output=WIRES
#
# The Part Studio rebuild can take minutes after a push; set OS_TIMEOUT (seconds, default 120).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
KEYS="$HERE/../onshape-keys.env"
AK=$(awk -F= '/^ONSHAPE_ACCESS_KEY/{print $2}' "$KEYS" | tr -d ' \t\r')
SK=$(awk -F= '/^ONSHAPE_SECRET_KEY/{print $2}' "$KEYS" | tr -d ' \t\r')
# "Pentagon skin" document, main workspace, Part Studio 1
DOC=ef4e23f1db1545ca302d904d
WS=00b81bd11d173342d88b8eaf
PS=b35857b7883c52300e570341
BASE=https://cad.onshape.com/api/v10
ACCEPT='Accept: application/json;charset=UTF-8; qs=0.09'

api() {
    if [ -n "${3:-}" ]; then
        curl -sS -m "${OS_TIMEOUT:-120}" -u "$AK:$SK" -X "$1" -H "$ACCEPT" -H 'Content-Type: application/json' --data-binary "@$3" "$BASE$2"
    else
        curl -sS -m "${OS_TIMEOUT:-120}" -u "$AK:$SK" -X "$1" -H "$ACCEPT" "$BASE$2"
    fi
}

case "${1:-}" in
api)
    shift; api "$@" ;;
push)
    TMP=$(mktemp)
    api GET "/featurestudios/d/$DOC/w/$WS/e/$2" | python3 -c "
import sys, json
old = json.load(sys.stdin)
json.dump({'contents': open(sys.argv[1]).read(), 'sourceMicroversion': old['sourceMicroversion'],
           'rejectMicroversionSkew': False, 'serializationVersion': old['serializationVersion']}, open(sys.argv[2], 'w'))
" "$3" "$TMP"
    api POST "/featurestudios/d/$DOC/w/$WS/e/$2" "$TMP" | python3 -c "
import sys, json
d = json.load(sys.stdin); print('pushed', len(d.get('contents', '')), 'characters')"
    rm -f "$TMP" ;;
view)
    api GET "/parts/d/$DOC/w/$WS/e/$PS/partid/$2/shadedviews?outputHeight=1200&outputWidth=1200&pixelSize=0&edges=show&viewMatrix=${4:-isometric}" | python3 -c "
import sys, json, base64
open(sys.argv[1], 'wb').write(base64.b64decode(json.load(sys.stdin)['images'][0])); print('saved', sys.argv[1])" "$3" ;;
parts)
    api GET "/parts/d/$DOC/w/$WS/e/$PS" | python3 -c "
import sys, json
for p in json.load(sys.stdin): print(p.get('partId'), p.get('bodyType'), '|', p.get('name'))" ;;
status)
    api GET "/partstudios/d/$DOC/w/$WS/e/$PS/features" | python3 -c "
import sys, json
d = json.load(sys.stdin); states = d.get('featureStates', {})
for f in d['features']:
    print(f.get('featureId'), '|', f.get('name'), '|', states.get(f.get('featureId'), {}).get('featureStatus'))" ;;
message)
    TMP=$(mktemp)
    python3 -c "
import json, sys
json.dump({'script': 'function(context is Context, queries) { return getFeatureStatus(context, makeId(\"' + sys.argv[1] + '\")); }', 'queries': {}}, open(sys.argv[2], 'w'))" "$2" "$TMP"
    api POST "/partstudios/d/$DOC/w/$WS/e/$PS/featurescript" "$TMP" | python3 -c "
import sys, json
d = json.load(sys.stdin)
def flat(x):
    if isinstance(x, dict):
        if 'key' in x and 'value' in x: return (flat(x['key']), flat(x['value']))
        if 'value' in x:
            v = flat(x['value'])
            return dict(v) if isinstance(v, list) and v and all(isinstance(i, tuple) for i in v) else v
        return {k: flat(v) for k, v in x.items() if k not in ('btType', 'typeTag')}
    if isinstance(x, list): return [flat(i) for i in x]
    return x
r = flat(d.get('result')) or {}
print(r.get('statusType'), '|', r.get('statusMsg'))
for n in d.get('notices', []): print('notice:', n.get('message'))"
    rm -f "$TMP" ;;
set)
    FID=$2; shift 2
    TMP=$(mktemp)
    api GET "/partstudios/d/$DOC/w/$WS/e/$PS/features" | python3 -c "
import json, sys
d = json.load(sys.stdin)
f = [x for x in d['features'] if x.get('featureId') == sys.argv[1]][0]
want = dict(a.split('=', 1) for a in sys.argv[3:])
for p in f['parameters']:
    k = p['parameterId']
    if k in want:
        if 'expression' in p: p['expression'] = want[k]
        elif isinstance(p.get('value'), bool): p['value'] = want[k] == 'true'
        else: p['value'] = want[k]
json.dump({'btType': 'BTFeatureDefinitionCall-1406', 'feature': f, 'serializationVersion': d['serializationVersion'],
           'sourceMicroversion': d['sourceMicroversion'], 'rejectMicroversionSkew': False}, open(sys.argv[2], 'w'))" "$FID" "$TMP" "$@"
    api POST "/partstudios/d/$DOC/w/$WS/e/$PS/features/featureid/$FID" "$TMP" > /dev/null && echo "updated $FID"
    rm -f "$TMP" ;;
*)
    sed -n '2,12p' "$0" ;;
esac
