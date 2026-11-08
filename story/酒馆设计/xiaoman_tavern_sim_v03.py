"""小满酒馆 v0.3 候选经济切片。Python 3.10+，仅标准库。金额均为整数 CR。
python xiaoman_tavern_sim_v03.py --help
不读取游戏、存档或发布目录；假设实际营业且正常收付款。"""
from argparse import ArgumentParser
from dataclasses import dataclass

NPC = (15, 17, 19, 20, 21, 16, 14, 13, 12, 11, 10, 9)
CAP = (0, 2, 5, 9, 14)
SLOPE = (0, 1, 2, 3, 4)
RESERVE = 900
PROPERTY_PRICE = 9800
PATHS = {'none': (), 'low': (5, 8, 11), 'active': (4, 6, 8, 10)}
def up(n, d):
    return (n + d - 1) // d

@dataclass
class Account:
    cash: int = RESERVE
    principal: int = 8800
    fees: int = 0
    bills: int = 0
    swept: int = 0
    regulars: int = 0

def trade_week(a, w, level, cups=0, extension_fee=0):
    assert 4 <= w <= 15 and 0 <= level <= 4 and 0 <= cups <= 7
    old = a.cash - a.principal - a.fees - a.bills
    a.regulars = min(CAP[level], a.regulars + SLOPE[level])
    rent = 90 if w <= 8 else 260 if w <= 10 else 420
    charge = up(a.principal, 100) + (240 if w == 9 else 0) + extension_fee
    margin = 35*(NPC[w-4]+a.regulars) + 18*cups - 345 - rent
    a.principal += 420 - rent
    a.fees += charge
    remaining = a.cash + margin - a.bills
    a.cash, a.bills = max(0, remaining), max(0, -remaining)
    sweep = min(max(0, a.cash-RESERVE), a.principal+a.fees)
    a.cash -= sweep
    pay_fee = min(sweep, a.fees)
    a.fees -= pay_fee
    a.principal -= sweep - pay_fee
    a.swept += sweep
    assert a.cash - a.principal - a.fees - a.bills == old + margin - (420-rent) - charge
    return margin, sweep

def simulate_pre(path, cups=0, extend=0):
    a, rows, eligible = Account(), [], 0
    for w in range(4, 14+extend):
        if w == 14 and eligible < 1 or w == 15 and eligible < 2:
            raise ValueError("冻结的回款记录不足以取得所选延期")
        level = sum(w>=u for u in PATHS[path])
        actual_cups = min(cups,7-int(w in PATHS[path]))  # 项目占一晚
        margin, sweep = trade_week(a,w,level,actual_cups,{14:80,15:120}.get(w,0))
        rows.append((w,level,a.regulars,margin,sweep,a.swept,a.cash,a.principal,a.fees,a.bills))
        if w==13:
            eligible = int(a.swept>=1400) + int(a.swept>=2250)
    return a,eligible,rows

def refinance(a):
    return a.principal + a.fees + a.bills + max(0,RESERVE-a.cash) + 240

def repay(d, bar, budget, rate_bp=50, max_weeks=104):
    """budget 是每周可投入的 CR，或逐周预算序列；返回首个清零周、结余及最低履约余量。"""
    payment = up(d,52)
    balance = d
    slack = None
    for t in range(1,max_weeks+1):
        y = budget if isinstance(budget,int) else budget[t-1]
        if min(y,bar)<0:
            raise ValueError("本切片只接受非负可用款；负经营现金须另跑欠费/违约状态机。")
        interest = up(balance*rate_bp,10000)
        due = min(payment,balance)+interest
        margin = bar+y-due
        slack = margin if slack is None else min(slack,margin)
        if margin<0:
            return None,0,slack
        total = balance + interest
        from_bar = min(bar,total)
        from_player = min(y,total-from_bar)
        balance = total-from_bar-from_player
        if balance==0:
            return t,y-from_player,slack
    return None,0,slack

def second_stage(left,y,price=PROPERTY_PRICE):
    if left>=price: return 0
    return up(price-left,y) if y else None

def joint_band(d,bar,bp):
    ys=[]
    for y in range(1,4501):
        weeks,left,_=repay(d,bar,y,bp)
        if weeks is not None and 5<=weeks<=8 and 5<=second_stage(left,y)<=8:
            ys.append(y)
    groups=[]
    for y in ys:
        if not groups or y>groups[-1][-1]+1: groups.append([y,y])
        else: groups[-1][-1]=y
    return groups

def case(path,cups,y,bp,extend=0,stress=False):
    a,eligible,_=simulate_pre(path,cups,extend)
    d=refinance(a); bar=350 if stress else 420+35*a.regulars
    weeks,left,slack=repay(d,bar,y,bp)
    return (d,eligible,bar,weeks,left,None if weeks is None else second_stage(left,y),slack)

def selftest():
    for path in PATHS:
        for cups in range(8):
            a,eligible,_=simulate_pre(path,cups)
            for ext in range(eligible+1):
                a,_,_=simulate_pre(path,cups,ext)
                d=refinance(a)
                for rate in [35,50]:
                    t,_,slack=repay(d,350,0,rate)
                    assert t is not None and t<=52 and slack>=0

    a,eligible,rows=simulate_pre("low")
    assert rows[-1][5:] == (1550,775,9875,541,0) and eligible==1
    assert refinance(a)==10781
    assert case("low",0,1500,50)[3:6]==(5,232,7)
    assert case("low",0,0,50)[3]==16
    assert case("low",0,0,50,stress=True)[3]==34
    assert case("none",0,0,50)[:2]==(12374,0)
    assert simulate_pre("active")[1]==2
    worst=Account()
    for w in range(4,16):
        trade_week(worst,w,0,0,{14:80,15:120}.get(w,0))
    assert refinance(worst)==13643 and repay(13643,350,0,50)==(44,0,18)
    assert up(14404,52)+up(14404*50,10000)==350
    assert up(14405,52)+up(14405*50,10000)>350

def main():
    ap=ArgumentParser(description=__doc__)
    ap.add_argument('--path',choices=PATHS,default='low')
    ap.add_argument('--cups',type=int,choices=range(8),default=0,help='目标普通杯/周，项目当晚不能点单')
    ap.add_argument('--extension',type=int,choices=(0,1,2),default=0)
    ap.add_argument('--weekly',type=int,default=1500,help='玩家周可用款；已扣原生账单、补货和保留金')
    ap.add_argument('--owner',action='store_true',help='自愿签署产权人有限担保，35基点/周')
    ap.add_argument('--stress',action='store_true',help='后期散客和旧客同时为零，只有20份保底订单')
    ap.add_argument('--band',action='store_true',help='枚举两段均5–8周的固定周投入')
    a=ap.parse_args()
    selftest()
    if a.weekly<0: ap.error("周投入必须非负")
    acc,eligible,rows=simulate_pre(a.path,a.cups,a.extension)
    print("财周 阶段 熟客 净现金 当期清偿 累计清偿 现金 本金 融资应付 经营欠款")
    for row in rows: print(*row)
    bp=35 if a.owner else 50
    d,e,bar,w,left,p,slack=case(a.path,a.cups,a.weekly,bp,a.extension,a.stress)
    print(f"可延期={eligible}周  重组本金={d}  酒馆周余={bar}  清债周数={w}  末周玩家余款={left}  另攒物业周数={p}  最小履约余量(含玩家款)={slack}")
    if a.band: print("联合窗口 CR/周",joint_band(d,bar,bp))
if __name__=='__main__': main()
