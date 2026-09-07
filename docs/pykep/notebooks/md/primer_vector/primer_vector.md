# Primer Vector

In this notebook we revisit the primer vector theory from Lawden from the lens of first order variations. We then construct manually, for a specific test case, the state transition matrices needed to call {func}`pykep.trajopt.primer_vector` and show
its use. The same plot can also be obtained, without having to go through the math intensive developments, using the {func}`pykep.trajopt.pl2pl_N_impulses.plot_primer_vector` method of the Multiple Impulse Transfer UDP `pykep.trajopt.pl2pl_N_impulses`

Classically, the result of the primer vector is derived using Pontryagin maximum principle, here we present an original derivation building on the work from Bauregard, Acciarini and Izzo {cite:p}`beauregard`, which allows also to extend the primer vector to new, previously untreated, cases (see the notebook [A primer vector surrogate](<./surrogate_primer_vector.ipynb>)).

:::{note}
  The developments are here shown in details as they can be extended to more generic cases that use different dynamics and number of impulses. The user must in that case provide the state ransition matrices and DVs on a time grid as constructed below.

  ## Theory and notation

  ------------

  a) Consider the following definition of the State Transition Matrix, $\mathbf M_{fs}$:

  $$
  \mathbf M_{fs} := \frac{\partial \mathbf x_{f}}{\partial \mathbf x_{s}}
  $$

  Note how this definition does not depend on the dynamics. The STM allows to describe variations of the (final) state at $f$ as:

  $$
  \delta\mathbf x_f = \mathbf M_{fs} \delta \mathbf x_s
  $$

  We also make use of the following definitions for the various blocks of the STM:

  $$
  \mathbf M = \left[ 
  \begin{array}{c|c} 
    \mathbf M^{rr} & \mathbf M^{rv} \\ 
    \hline 
    \mathbf M^{vr} & \mathbf M^{vv} 
  \end{array} 
  \right] 
  = \left[ 
  \begin{array}{c|c} 
    \mathbf M^{xr} & \mathbf M^{xv} 
  \end{array} 
  \right] 
  = \left[ 
  \begin{array}{c} 
    \mathbf M^{rx} \\
    \hline 
    \mathbf M^{vx} 
  \end{array} 
  \right] 
  $$

  ---------------

  b) Assume now to have a grid of $N$ points along a multiple impulse trajectory and pick three indexes $i,j,k$. 

  We ask the following question: what happens when we add three small $\delta\Delta V$ at the selected nodes?

  To answer this question, we compute the variation of the final state due to intermidiate variations at the nodes using the STMs:

  $$
  \delta \mathbf x_f = \mathbf M_{fi}\delta\mathbf x_i + \mathbf M_{fj}\delta\mathbf x_j + \mathbf M_{fk}\delta\mathbf x_k
  $$

  which we set to zero, as we do not want the trajectory boundary cnstraints to change, only find a better $\Delta V$:

  $$
  \mathbf 0 = \mathbf M_{fi}\delta\mathbf x_i + \mathbf M_{fj}\delta\mathbf x_j + \mathbf M_{fk}\delta\mathbf x_k
  $$

  which becomes (multiplying by $\mathbf M_{jf}$):

  $$
  \mathbf M_{ji}\delta\mathbf x_i + \mathbf M_{jk}\delta\mathbf x_k + \delta\mathbf x_j = \mathbf 0
  $$

  --------------

  c) The state variations $\delta\mathbf x$ are, in our case, consequence of three $\delta\Delta \mathbf V$, so that the previous equations becomes:

  $$
  \begin{align}
  \mathbf M_i^{rv} \delta\Delta \mathbf V_i + \mathbf M_k^{rv} \delta\Delta \mathbf V_k = \mathbf 0\\
  \mathbf M_i^{vv} \delta\Delta \mathbf V_i + \mathbf M_k^{vv} \delta\Delta \mathbf V_k + \delta\Delta \mathbf V_j = \mathbf 0\\
  \end{align}
  $$

  which becomes:

  $$
  \begin{align}
  \delta\Delta \mathbf V_i &=& - (\mathbf M_i^{rv})^{-1}\mathbf M_k^{rv} \delta\Delta \mathbf V_k = \mathbf A_{ik}\delta\Delta \mathbf V_k\\
  \delta\Delta \mathbf V_j &=& - \big(\mathbf M_i^{vv}\mathbf A_{ik} + \mathbf M_k^{vv} \big)  \delta\Delta \mathbf V_k = \mathbf A_{jk}\delta\Delta \mathbf V_k\\
  \end{align}
  $$

  The matrices $\mathbf A$ are telling us how the three variations of impulsive velocity changes applied in ($i$, $j$, $k$) must be related for the overall trajectory to not change its boundary conditions (i.e. $\mathbf x_f = \mathbf 0$).

  -------------

  d) So far the three indexes we picked (i.e. $i,j,k$) were completely equivalent, now we assume that in $i,j$ a finite $\Delta V$ is already present, while in $k$ only our additional $\delta\Delta \mathbf V$ will be present.

  The total magnitude of the $\Delta \mathbf V$ can then be expressed by:

  $$
  J = \Delta V_{tot} = |\mathbf V_i + \delta\Delta \mathbf V_i | + |\mathbf V_j + \delta\Delta \mathbf V_j | + |\delta\Delta \mathbf V_k|
  $$

  and its first order variation:

  $$
  \delta J = \frac{\Delta\mathbf V_i}{|\Delta \mathbf V_i|}\cdot \delta\Delta \mathbf V_i + \frac{\Delta \mathbf V_j}{|\Delta \mathbf V_j|}\cdot \delta\Delta \mathbf V_j + |\delta\Delta \mathbf V_k|
  $$

  e) The primer vector

  We introduce $\hat{\mathbf u} = \frac{\delta \Delta \mathbf V_k}{|\delta \Delta \mathbf V_k|}$ as the unit vector along the direction of the $\delta \Delta V$ added in $k$. Substituting and regrouping we have:

  $$
  \delta J = |\delta\Delta \mathbf V_k| \left(1 + \left(\mathbf A_{ik}^T\frac{\Delta\mathbf V_i}{|\Delta \mathbf V_i|} + \mathbf A_{jk}^T\frac{\Delta\mathbf V_j}{|\Delta \mathbf V_j|} \right) \cdot \hat {\mathbf u}\right)
  $$

  which we rewrite as:

  $$
  \delta J = |\delta\Delta \mathbf V_k| \left(1 - \mathbf p\cdot \hat {\mathbf u}\right)
  $$

  where we have introduced the vector:

  $$
  \boxed{\mathbf p = - \mathbf A_{ik}^T\frac{\Delta\mathbf V_i}{|\Delta \mathbf V_i|} - \mathbf A_{jk}^T\frac{\Delta\mathbf V_j}{|\Delta \mathbf V_j|}}
  $$

  which is of fundamental importance in space flight mechanics and is called **primer vector**. 

  From the expression of $\delta J$ it appears clear that if we want to be able to decrease the overall $\Delta V$ adding in $k$ one impulse it is necessary for the norm of the primer vector (computed in $k$) to be larger than 1.

Enough of that, let us start coding now ...


```python
import pykep as pk
import numpy as np

np.set_printoptions(legacy="1.25")
import matplotlib.pyplot as plt
```

Let us define a toy problem. In the Keplerian dynamics, we have the following (optimal) four-impulse trajectory which was obtained using the [pl2pl_N_impulses UDP](<./udp_pl2pl_N_impulses.ipynb>).


```python
# Problem data
start = pk.planet(pk.udpla.jpl_lp("earth"))
target = pk.planet(pk.udpla.jpl_lp("venus"))

r0 = np.array([-77310392520.5891, -130158155639.95819, 147108.35686371813])
v0 = np.array([25126.38412487125, -15324.0242317188, 0.017319637130567115])
DV0 = np.array([131.74444122221112, -111.57168023031436, -96.28585532081512])
dt0 = (194.835548685441557 + 14.82906396200053) * pk.DAY2SEC

r1 = np.array([27011591791.503845, 148104382453.56558, 170324664.00757253])
v1 = np.array([-29342.408370789373, 5003.190386138956, 90.37256194347349])
DV1 = np.array([2564.347941748753, -50.33730074112419, 941.8690690439083])
dt1 = 102.51706391196915 * pk.DAY2SEC

r2 = np.array([-120164601140.7896, -15645977554.833487, 4332410828.357129])
v2 = np.array([9183.937186025161, -32921.84916571874, -601.1091614146442])
DV2 = np.array([-45.308371681150675, 200.42244183402727, -105.55464659459722])
dt2 = 47.818323440588806 * pk.DAY2SEC

r3 = np.array([-13587329395.522686, -107835070067.45769, -689845413.6226778])
v3 = np.array([34510.778377374605, -4515.1531552484175, -2053.713672761537])
DV3 = np.array([-2709.616020196663, -5.353417557126704, -607.7075255532395])
```

We now define the grid where the prime vector will be computed, this will include 3 different segments, starting at the first impulse and ending at the third impulse.


```python
# The grid for the first segment
tgrid0 = np.linspace(0, dt0, 104)
# The grid for the second segment
tgrid1 = np.linspace(dt0, dt0 + dt1, 51)
# The grid for the third segment
tgrid2 = np.linspace(dt0 + dt1, dt0 + dt1 + dt2, 23)
# The overall grid concatented
tgrid = np.concatenate((tgrid0, tgrid1[1:], tgrid2[1:]))
```

Note that the three grids are uniform, but with three `dts` all roughly (not exactly) 2 days. 
We compute the trajectory STMs at all grid points

We now first compute the trajectory as well as the STMs. 
We need to propagate at each impulse, add the impulse, then proceed. The various STMs thus computed will be:

$\underbrace{M_{00}, M_{10}, M_{20}, ... M_{n0}}_{\text{First grid}} | \underbrace{M_{nn}, M_{(n+1)n}M_{(n+2)n}, M_{mn}}_{\text{Second grid}} | \underbrace{M_{mm}, M_{(m+1)m}, ...  M_{fm}}_{\text{Third grid}}$

Instead we want:

$M_{00}, M_{10}, M_{20}, ... M_{f0}$

thus some extra manipulations will be necessary.


```python
# First segment
retval0 = pk.propagate_lagrangian_grid([r0, v0 + DV0], tgrid0, mu=pk.MU_SUN, stm=True)

# Second segment
retval1 = pk.propagate_lagrangian_grid(
    [retval0[-1][0][0], retval0[-1][0][1] + DV1], tgrid1, mu=pk.MU_SUN, stm=True
)

# Third segment
retval2 = pk.propagate_lagrangian_grid(
    [retval1[-1][0][0], retval1[-1][0][1] + DV2], tgrid2, mu=pk.MU_SUN, stm=True
)

# We assemble all the trajectory in the same grid (these are the extra manipulations)
stms = [item[1] for item in retval0]
posvels = [item[0] for item in retval0]
Mn0 = retval0[-1][1]
Mmn = retval1[-1][1]
retval1.pop(0)
retval2.pop(0)
for item in retval1:
    stms.append(item[1] @ Mn0)
    posvels.append(item[0])
for item in retval2:
    stms.append(item[1] @ Mmn @ Mn0)
    posvels.append(item[0])
#posvels[-1][1] = [a + b for a, b in zip(posvels[-1][1], DV3)]

```

We visualize now the whole trajectory.


```python
ax = pk.plot.make_3Daxis()
ax.plot(
    [item[0][0] for item in posvels],
    [item[0][1] for item in posvels],
    [item[0][2] for item in posvels],
)
ax.scatter3D(
    posvels[0][0][0], posvels[0][0][1], posvels[0][0][2], "ko", c="k", alpha=0.5
)
ax.scatter3D(
    posvels[103][0][0], posvels[103][0][1], posvels[103][0][2], "ko", c="k", alpha=0.5
)
ax.scatter3D(
    posvels[153][0][0], posvels[153][0][1], posvels[153][0][2], "ko", c="k", alpha=0.5
)
ax.scatter3D(
    posvels[175][0][0], posvels[175][0][1], posvels[175][0][2], "ko", c="k", alpha=0.5
)

pk.plot.add_planet_orbit(ax, start, units=1.0, alpha=0.3)
pk.plot.add_planet_orbit(ax, target, units=1.0, alpha=0.3)
ax.axis("off")
ax.view_init(90, 0)
```


    
![png](output_10_0.png)
    


We are ready to compute the primer vector.


```python
res = []
# The gridpoints where the impulses are applied have idxs: 0 (DV0), 103 (DV1), 153 (DV2), 175 (DV3)
idx_i = 0
idx_j = 175
DVi = DV0
DVj = DV3
for idx_k, _ in enumerate(tgrid):
    Mji = stms[idx_j] @ np.linalg.inv(stms[idx_i])
    Mjk = stms[idx_j] @ np.linalg.inv(stms[idx_k])
    res.append(np.linalg.norm(pk.trajopt.primer_vector(DVi, DVj, Mji, Mjk)[0]))
```

... and visualise it.


```python
plt.plot(res, label = 'primer vector magnitude')
plt.vlines(0, 0.8, 1.2, 'k', linestyles="dashed", label = 'impulse')
plt.vlines(103, 0.8, 1.2, 'k', linestyles="dashed")
plt.vlines(153, 0.8, 1.2, 'k', linestyles="dashed")
plt.vlines(175, 0.8, 1.2, 'k', linestyles="dashed")
plt.hlines(1, 0, 200, "r")
plt.legend(loc="lower right")
```




    <matplotlib.legend.Legend at 0x30900fcb0>




    
![png](output_14_1.png)
    


In this case the trajectory looks quite optimal as shown by the primer vector magnitude being consistently less than one.
Note also how at each impulse, except the starting and ending ones, the deriative of the primer vector magnitude flattens.
The fact this is not the case at the starting and ending conditions, hints to the suboptimality of the transfer time (e.g. extrapolating the primer vecotr before and/or after the set grid, its magnitude would be larger than one!).
