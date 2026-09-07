# Sims-Flanagan: planet to plane low-thrust transfer

In this tutorial we show the use of the {class}`pykep.trajopt.sf_pl2pl` to find a low-thrust trajectory connecting two moving planets. 
This naturally follows the previous notebook on the [point to point](<./udp_sf_point2point.ipynb>) version of the same problem.

The decision vector for this problem, compatible with pygmo {cite:p}`pagmo` UDPs (User Defined Problems), is:

$$
\mathbf x = [t_0, m_f, V_{sx}^\infty, V^\infty_{sy}, V^\infty_{sz}, V^\infty_{fx}, V^\infty_{fy}, V^\infty_{fz}, u_{x0}, u_{y0}, u_{z0}, u_{x1}, u_{y1}, u_{z1}, ..., T_{tof}]
$$

containing the starting epoch $t_0$ as a MJD2000, the final mass $m_f$ as well as the starting and final $V^{\infty}$, throttles and the time-of-flight $T_{tof}$.

:::{note}
This notebook makes use of the commercial solver SNOPT 7 and to run needs a valid `snopt_7_c` library installed in the system. In case SNOPT7 is not available, you can still run the notebook using, for example `uda = pg.algorithm.nlopt("slsqp")` with minor modifications.

Basic imports:


```python
import pykep as pk
import numpy as np
import time
import pygmo as pg
import pygmo_plugins_nonfree as ppnf
import time

from matplotlib import pyplot as plt
```

We start defining the problem data.


```python
# Problem data
mu = pk.MU_SUN
max_thrust = 0.6
isp = 3000
veff = isp * pk.G0

# Source and destination planets
earth = pk.planet_to_keplerian(
    pk.planet(pk.udpla.jpl_lp(body="EARTH")), when=pk.epoch(5000)
)

mars = pk.planet_to_keplerian(
    pk.planet(pk.udpla.jpl_lp(body="MARS")), when=pk.epoch(5000)
)

venus = pk.planet_to_keplerian(
    pk.planet(pk.udpla.jpl_lp(body="venus")), when=pk.epoch(5000)
)

# Initial state
ms = 1500.0

# Number of segments
nseg = 30
```

We here instantiate two different versions of the same UDP (User Defined Problem), with analytical gradients and without. 

For the purpose of this simple notebook we choose a relatively simple Earth to Mars transfer with an initial $V_{\infty}$ of 3 km/s.


```python
udp_nog = pk.trajopt.sf_pl2pl(
        pls=earth,
        plf=mars,
        ms=ms,
        mu=pk.MU_SUN,
        max_thrust=max_thrust,
        veff=veff,
        t0_bounds=[7360, 8300.0],
        tof_bounds=[100.0, 350.0],
        mf_bounds=[1000., ms],
        vinfs=0.,
        vinff=0.,
        nseg=nseg,
        cut=0.6,
        mass_scaling=ms,
        r_scaling=pk.AU,
        v_scaling=pk.EARTH_VELOCITY,
        with_gradient=False,
)

udp_g = pk.trajopt.sf_pl2pl(
        pls=earth,
        plf=mars,
        ms=ms,
        mu=pk.MU_SUN,
        max_thrust=max_thrust,
        veff=veff,
        t0_bounds=[7360, 8300.0],
        tof_bounds=[100.0, 350.0],
        mf_bounds=[1000., ms],
        vinfs=0.,
        vinff=0.,
        nseg=nseg,
        cut=0.6,
        mass_scaling=ms,
        r_scaling=pk.AU,
        v_scaling=pk.EARTH_VELOCITY,
        with_gradient=True,
)
```

## Analytical performances of the analytical gradient

And we take a quick look at the performances of the analytical gradient with respect to the numerically computed one.


```python
# We need to generste a random chromosomes compatible with the UDP where to test the gradient.
prob_g = pg.problem(udp_g)
pop_g = pg.population(prob_g, 1)
```

First the analytical gradient:


```python
%%timeit
udp_g.gradient(pop_g.champion_x)
```

    630 μs ± 11.1 μs per loop (mean ± std. dev. of 7 runs, 1,000 loops each)
    

Then a simple numerical gradient based on finite differences:


```python
%%timeit
pg.estimate_gradient(udp_g.fitness, pop_g.champion_x)
```

    6.73 ms ± 20.9 μs per loop (mean ± std. dev. of 7 runs, 100 loops each)
    

Then a higher order numerical gradient:


```python
%%timeit
pg.estimate_gradient_h(udp_g.fitness, pop_g.champion_x)
```

    20.2 ms ± 144 μs per loop (mean ± std. dev. of 7 runs, 10 loops each)
    

The analytical gradient is, in this case, exact and faster, seems like a no brainer to decide wether one has to use it. 

In reality, the effects on the optimization technique used are not straightforward, making the option to use numerical gradients still interesting in some, albeit rare, cases.

## Solving the low-thrust transfer

We define (again) the optimization problem, and set a tolerance for *pagmo* to be able to judge the relative value of two individuals. 

:::{note}
This tolerance has a different role from the numerical tolerance set in the particular algorithm chosen to solve the problem and is only used by the *pagmo* machinery to decide outside the optimizer whether the new proposed indivdual is better than what was the previous *champion*.


```python
prob_g = pg.problem(udp_g)
prob_g.c_tol = 1e-6
```

... and we define an optimization algorithm.


```python
snopt72 = "/usr/local/lib/libsnopt7_c.so"
uda = ppnf.snopt7(library=snopt72, minor_version=2, screen_output=False)
uda.set_integer_option("Major iterations limit", 2000)
uda.set_integer_option("Iterations limit", 20000)
uda.set_numeric_option("Major optimality tolerance", 1e-3)
uda.set_numeric_option("Major feasibility tolerance", 1e-11)

#uda = pg.nlopt("slsqp")
algo = pg.algorithm(uda)
```

We solve the problem from random initial guess ten times and only save the result if a feasible solution is found (as defined by the criterias above)


```python
masses = []
xs = []
for i in range(10):
    pop_g = pg.population(prob_g, 1)
    pop_g = algo.evolve(pop_g)
    if(prob_g.feasibility_f(pop_g.champion_f)):
        print(".", end="")
        masses.append(pop_g.champion_x[1])
        xs.append(pop_g.champion_x)
    else:
        print("x", end ="")
print("\nBest mass is: ", np.max(masses))
print("Worst mass is: ", np.min(masses))
best_idx = np.argmax(masses)
```

    .........x
    Best mass is:  1234.15677161
    Worst mass is:  1055.94065222
    

And we plot the trajectory found:


```python
udp_g.pretty(xs[best_idx])
```

    
    Low-thrust NEP transfer
    Departure: earth(jpl_lp)(K)
    Arrival: mars(jpl_lp)(K)
    
    Launch epoch: 7451.99290 MJD2000, a.k.a. 2020-05-26T23:49:46.639162
    Arrival epoch: 7801.99290 MJD2000, a.k.a. 2021-05-11T23:49:46.639162
    Time of flight (days): 350.00000 
    
    Launch DV (km/s) 0.00000000 - [-0.0,-0.0,-0.0]
    Arrival DV (km/s) 0.00000000 - [-0.0,-0.0,-0.0]
    Final mass (kg): 1234.156771610159
    
    Details on the low-thrust leg: 
    Number of segments: 30
    Number of fwd segments: 18
    Number of bck segments: 12
    Maximum thrust: 0.6
    Central body gravitational parameter: 1.3271244004127942e+20
    Specific impulse: 29419.949999999997
    
    Time of flight: 30240000
    Initial mass: 1500
    Final mass: 1234.156771610159
    State at departure: [[-61954526368.884995, -138336261328.15906, 4315628.631202786], [26701.920353639984, -12287.534991671208, 0.38332999105107457]]
    State at arrival: [[-154565138881.2139, 191757916330.46805, 7812274607.089294], [-17948.711153788554, -13142.621427747503, 165.25507018889533]]
    Throttles values: [0.9150544648347976, -0.2727543389414908, -0.29712018218766784, 0.9624610630773269, -0.10203797729041932, -0.25150935164371235, 0.9776014444167017, 0.07614377352808915, -0.19620790610918598, 0.957702324318249, 0.2552571227935213, -0.13285352754629626, 0.9020195250406122, 0.4270367782902017, -0.06324845384866676, 0.8125740078618441, 0.582769461576093, 0.010160722844625271, 0.6250556058421802, 0.643431656576734, 0.07588141128543445, 7.598343716549538e-06, 1.1185880791432576e-05, 2.2453332061872086e-06, 1.4408745246271949e-05, 3.1565395204124255e-05, 8.088752742965002e-06, 4.167876473489592e-06, 1.5868867168138716e-05, 4.815375942141084e-06, 8.545615563570315e-06, 8.944395789605744e-05, 3.19946851569267e-05, -8.669356237676905e-05, 0.0013694500186129483, 0.0005403961740439303, -0.19815756048776179, 0.8908099690924527, 0.408890181917974, -0.32595401927831397, 0.8403740439172253, 0.433042081594063, -0.4419725332619276, 0.7766744775427021, 0.4488173756337557, -0.16721227800602773, 0.21506256326908357, 0.13984526500303546, -0.0008577982888386832, 0.0008253804748373212, 0.000611955943109358, -1.9689873107124022e-05, 1.4484973841899776e-05, 1.2436117595143888e-05, -2.8620412636608553e-06, 1.4743737492308882e-06, 1.3706037810142675e-06, -2.402979236322631e-06, 1.1913160319104661e-06, 1.2756146936779317e-06, -4.244191704846397e-07, 1.0828530514628123e-07, 1.8453337167945703e-07, -9.37373180281157e-07, 8.370732519658679e-08, 4.5982001574680147e-07, -5.02625458610645e-06, 7.7773055659164e-08, 1.7093668556714202e-06, -6.066420090578855e-07, -9.776882662778539e-08, 1.693196783758444e-07, -2.4279305758096285e-06, -5.020957271186525e-07, 5.687923131273488e-07, -1.763015410795579e-06, -5.570191621396674e-07, 3.671370129445859e-07, -6.662472253806185e-07, -3.989017097670747e-07, 1.796905964689047e-07, -0.6703382738852394, -0.37712488780496517, 0.07550759065615623, -0.8176963270878755, -0.5736343129475407, 0.04812890714381351, -0.7587432384564023, -0.6513895542677574, -0.0005888145804974916]
    
    Mismatch constraints: [-43.6181640625, 15.74029541015625, 10.302734851837158, -5.042853445047513e-06, 5.885649443371221e-07, 1.9215204929423635e-06, -5.930804263698519e-06]
    Throttle constraints: [5.6887403676597614e-09, 7.136988777745046e-10, 7.924696454608693e-10, 5.228979471638695e-10, 4.812135134812934e-10, 3.887074884190156e-09, -0.18954320434151561, -0.9999999998120997, -0.999999998730586, -0.99999999970762, -0.999999990903091, -0.9999978250628478, 6.818712261491555e-10, 8.047968957924923e-10, 8.949667673618933e-10, -0.9062314498203972, -0.9999982084390912, -0.9999999992478374, -0.9999999999877563, -0.9999999999911793, -0.9999999999997741, -0.9999999999989029, -0.9999999999718088, -0.9999999999995938, -0.9999999999935295, -0.9999999999964467, -0.9999999999993647, -0.4027220213159526, 2.665689891045986e-11, 1.5066836667188e-11]
    
    
    


```python
ax = udp_g.plot(xs[best_idx], show_gridpoints=True)
ax.view_init(90, 0)
```


    
![png](output_24_0.png)
    


## Graident vs no gradient
We here take a look at the difference in solving the problem using the analytical gradient and not using it.
To do so we solve the same problem starting from 100 different initial guesses using both versions and compare.


```python
from tqdm import tqdm

cpu_nog = []
cpu_g = []

fail_g = 0
fail_nog = 0

prob_g = pg.problem(udp_g)
prob_nog = pg.problem(udp_nog)
prob_g.c_tol = 1e-6
prob_nog.c_tol = 1e-6

for i in tqdm(range(100)):
    pop_g = pg.population(prob_g, 1)
    pop_nog = pg.population(prob_nog)
    pop_nog.push_back(pop_g.champion_x)

    start = time.time()
    pop_g = algo.evolve(pop_g)
    end = time.time()
    if not prob_g.feasibility_f(pop_g.champion_f):
        fail_g += 1
    else: # We only record the time for successfull attempts
        cpu_g.append(end - start)

    start = time.time()
    pop_nog = algo.evolve(pop_nog)
    end = time.time()
    if not prob_nog.feasibility_f(pop_nog.champion_f):
        fail_nog += 1
    else: # We only record the time for successfull attempts
        cpu_nog.append(end - start)

print(f"Gradient: {np.median(cpu_g):.4e}s")
print(f"No Gradient: {np.median(cpu_nog):.4e}s")

print(f"\nGradient (n.fails): {fail_g}")
print(f"No Gradient (n.fails): {fail_nog}")
```

      0%|          | 0/100 [00:00<?, ?it/s]

    100%|██████████| 100/100 [07:40<00:00,  4.60s/it]

    Gradient: 1.4176e+00s
    No Gradient: 3.0340e+00s
    
    Gradient (n.fails): 14
    No Gradient (n.fails): 37
    

    
    


```python
cpu_g = np.array(cpu_g)
cpu_nog = np.array(cpu_nog)
plt.hist(cpu_g[cpu_g < 2.2], bins=20, label="gradient", density=True, alpha=0.5)
plt.hist(cpu_nog[cpu_nog < 1.2], bins=20, label="no_gradient", density=True, alpha=0.5)
# plt.xlim([0,0.15])
plt.legend()
plt.title("point2point, 10 seg case")
plt.xlabel("CPU time")
plt.ylabel("N. occurrences")
```




    Text(0, 0.5, 'N. occurrences')




    
![png](output_27_1.png)
    

