"""Chapter 4 defaults: exported recruitment tables plus observed first pulls (LD log 15.18-27)."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'ResourcesExport/Config/LuaJSON'
DST = ROOT / 'UnityAssets/Assets/_EndlessWinter/Data/Heroes'


def load(name):
    return json.loads((SRC / ('game_config_default_' + name + '.json')).read_text(encoding='utf-8'))


def main():
    items = load('item_config')
    drops = load('drop_conf')
    search = load('systemconfig')['hero_search_conf']
    heroes = json.loads((DST / 'heroes.json').read_text(encoding='utf-8'))['heroes']
    hero_ids = {h['id'] for h in heroes}
    shard_ids = {h['id'] + 450000: h['id'] for h in heroes}

    def reward(item, amount, weight=1):
        row = items.get(str(item), {})
        resource = row.get('result_condition')
        resource = resource if isinstance(resource, list) and len(resource) == 2 and resource[0] in range(101, 108) else [0, 0]
        return dict(itemId=item, amount=amount, weight=weight,
                    heroId=item if item in hero_ids else 0, shardHeroId=shard_ids.get(item, 0),
                    resourceId=resource[0], resourceAmount=resource[1], iconName=row.get('icon', ''))

    pools = []
    for pool_id, drop_id, name in [(1, 800001, 'advanced'), (2, 800002, 'epic')]:
        settings = search['search_conf'][pool_id - 1]
        rates = drops[str(drop_id)]['rates']
        rewards = [reward(*rates[i:i+3]) for i in range(0, len(rates), 3)]
        first = [reward(50004, 1), reward(500006, 1), reward(100032, 2)] if pool_id == 2 else [reward(500000, 1), reward(500001, 1), reward(100012, 2)]
        fixed_rates = drops[str(settings['fixed_drop'])]['rates']
        fixed = [reward(*fixed_rates[i:i+3]) for i in range(0, len(fixed_rates), 3)]
        pools.append(dict(id=pool_id, keyId=settings['cost_tiem'], freeDailyLimit=settings.get('max_times', 1),
                          cooldownMs=settings['time_interval'] * 1000, rewardsPerRecruit=search['random_time'] + 1,
                          firstRewards=first, rewards=rewards, fixedRewards=fixed))
    data = dict(_source='Editable defaults: systemconfig.hero_search_conf, drop_conf, item_config; observed first pulls LD 222/231. Legacy resume XP uses the visible Chapter 4 entry balance.',
                unlockBuilding=318, tutorialHeroId=50004, upgradeHeroId=50006, tutorialHeroLevel=5,
                legacyResumeHeroXp=4000, pools=pools)
    (DST / 'recruitment_ch4.json').write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    survivor_rows = load('survivors')
    healthcare = dict(_source='Editable placeholders: min_healthy_count from systemconfig.survivor; recovery is 12 game hours at 0.4 real seconds per minute (visible FTUE note). Cold threshold/exposure and arrival names are temporary tuning.',
                      clinicId=307, sickbedType=43, minimumHealthy=load('systemconfig')['survivor']['min_healthy_count'],
                      coldThreshold=-45, coldExposureMs=60000, recoveryMs=288000,
                      arrivals=[dict(id=i, displayName=survivor_rows[str(i)]['normal_body'].removeprefix('3d_people_').title(),
                                     portrait=survivor_rows[str(i)]['pic']) for i in [3,4,5,6,7,10]])
    survivor_dst = DST.parent / 'Survivors' / 'healthcare_ch4.json'
    survivor_dst.write_text(json.dumps(healthcare, indent=2) + '\n', encoding='utf-8')
    print('Wrote recruitment_ch4.json')


if __name__ == '__main__':
    main()
