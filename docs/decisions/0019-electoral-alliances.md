# 0019: Electoral alliances (joint lists with a joint programme)

Status: accepted. Date: 2026-11-25.

## Context
The player asked for pre-election coalitions with their own programme, so small parties can get into parliament under majority systems, and noted
that a party that joins a coalition whose programme contradicts its own must lose support.

## Decision
1. **A joint list stands as one party.** Up to three parties can run together (`FormAlliance`; `LeaveAlliance` takes it back at a credibility cost).
   In the election the list gets the members' votes (less what each loses, see 3), competes as one candidate in the simulated districts (so the
   vote-splitting and the strategic desertion of decision 0017 no longer cut it down; its compass position is the members' mean) and is held to a
   higher threshold in a proportional count (x1.6 for two parties, x2.2 for three: 5 % becomes 8 % and 11 %). Its seats are then split among
   the members by their counted votes (largest remainder) and the parties sit and bargain separately again. A list stands for one election.
2. **A joint programme.** `AllianceMath.MergePlatform` merges the members' programmes: every law any member stands for, at the
   support-and-priority-weighted mean of the targets, with the priority averaged over all members (a law only one partner stresses counts less).
   The coherence of the list is one minus the mean distance between the members' programmes.
3. **Contradicting the own programme costs votes.** Each member loses `0.03 + 0.35 x d` of its voters, d being the distance of the joint
   programme from the member's own (the bigger partner's programme dominates, so the smaller party loses more); capped at 60 %. The player's party
   also loses `0.5 x d` credibility on joining. The preview shows each member's loss.
4. **Who joins.** A partner joins when its seats at the current poll do not fall, its programme is within 0.45 of the others', and it is not
   on a list already. Only a party that wins less than 75 % of its fair share looks for partners on its own. The player may form a list within
   8 quarters of the election (3 capital); the AI forms its own pairs 4 quarters ahead when both members gain and the pair wins more seats
   together than apart. Deterministic, no draws of the election's random stream.
5. **Results.** In the districts the lists win small parties many seats (the base pack at the start: open_net 2 -> 15 with social_alliance in
   the districts, 1 -> 20 with a runoff); under the proportional count they rarely pay, which is as it should be.

## Not covered
Joining a *government* whose policies contradict a partner's programme still costs support only through the existing mechanisms (loyalty,
commitments, fiscal blame); a direct support term for government partners is a candidate for a later decision.
