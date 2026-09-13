package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.domain.FriendRecommendation;
import com.example.jamkkaebi.friend.domain.FriendRequestStatus;
import com.example.jamkkaebi.friend.domain.Friendship;
import com.example.jamkkaebi.friend.dto.response.RecommendationsResponse;
import com.example.jamkkaebi.friend.repository.BlockRepository;
import com.example.jamkkaebi.friend.repository.FriendRecommendationRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import com.example.jamkkaebi.friend.repository.UserCount;
import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ThreadLocalRandom;
import java.util.function.Function;
import java.util.function.Predicate;
import java.util.stream.Collectors;

/**
 * 오늘의 추천 3명.
 *
 * <p>게임 일자(자정 기준)마다 뽑아 저장하고, 같은 날에는 몇 번 불러도 같은 사람을 준다. 차단 등으로 자리가
 * 비면 그 자리만 새로 채우지 않는다.
 *
 * <b>항상 제외</b> — 나 · 이미 친구 · 어느 방향이든 살아 있는 요청 · 어느 방향이든 차단 · 친구가 가득
 *       찬 사람 · 받은 요청함이 가득 찬 사람.
 *
 */
@Service
public class FriendRecommendationService {

    //한 번에 훑는 인원.
    private static final int CANDIDATE_SCAN_LIMIT = 500;

    private final FriendUsers friendUsers;
    private final UserRepository userRepository;
    private final FriendRecommendationRepository recommendationRepository;
    private final FriendshipRepository friendshipRepository;
    private final FriendRequestRepository friendRequestRepository;
    private final BlockRepository blockRepository;
    private final PlayerCardAssembler playerCardAssembler;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public FriendRecommendationService(FriendUsers friendUsers,
                                       UserRepository userRepository,
                                       FriendRecommendationRepository recommendationRepository,
                                       FriendshipRepository friendshipRepository,
                                       FriendRequestRepository friendRequestRepository,
                                       BlockRepository blockRepository,
                                       PlayerCardAssembler playerCardAssembler,
                                       FriendProperties properties,
                                       GameClock gameClock) {
        this.friendUsers = friendUsers;
        this.userRepository = userRepository;
        this.recommendationRepository = recommendationRepository;
        this.friendshipRepository = friendshipRepository;
        this.friendRequestRepository = friendRequestRepository;
        this.blockRepository = blockRepository;
        this.playerCardAssembler = playerCardAssembler;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    /**
     * 오늘의 추천 친구 3명을 준다. 
     *
     * <p>하루에 추천하는 사람은 <b>저장된 행 기준으로 3명</b>이다. 뽑힌 사람과 차단 관계가 되면 응답에서는
     * 빼지만 행은 그대로 두어 그 자리를 다시 채우지 않는다.
     * 
     * <p>뽑기 전에 내 사용자 행을 잠근다 — 조회가 동시에 두 번 오면 같은 빈 자리를 두 번 채울 수 있다.
     */
    @Transactional
    public RecommendationsResponse today(String myFriendCode) {
        User me = friendUsers.me(myFriendCode);
        LocalDate today = gameClock.today();
        friendUsers.lock(me.getId());

        List<FriendRecommendation> picks =
                recommendationRepository.findAllByUserIdAndRecommendedDateOrderByIdAsc(me.getId(), today);
        Set<Long> pickedToday = picks.stream()
                .map(FriendRecommendation::getRecommendedUserId)
                .collect(Collectors.toSet());
        Set<Long> blocked = new HashSet<>(blockRepository.findCounterpartIds(me.getId()));
        Map<Long, User> recommended = new HashMap<>(friendUsers.findAll(pickedToday));
        List<FriendRecommendation> visible = new ArrayList<>(picks.stream()
                .filter(pick -> !blocked.contains(pick.getRecommendedUserId()))
                .filter(pick -> recommended.containsKey(pick.getRecommendedUserId()))
                .toList());

        // 차단으로 빈 자리는 채우지 않는다.
        int missing = properties.recommendationCount() - picks.size();
        if (missing > 0) {
            for (User user : pickFor(me.getId(), today, missing, pickedToday)) {
                visible.add(recommendationRepository.save(FriendRecommendation.of(me.getId(), user.getId(), today)));
                recommended.put(user.getId(), user);
            }
        }

        List<Long> recommendedIds = visible.stream().map(FriendRecommendation::getRecommendedUserId).toList();
        Map<Long, PlayerCollection> collections = playerCardAssembler.collectionsOf(recommendedIds);
        List<RecommendationsResponse.RecommendedPlayer> players = visible.stream()
                .map(pick -> {
                    User user = recommended.get(pick.getRecommendedUserId());
                    return new RecommendationsResponse.RecommendedPlayer(
                            playerCardAssembler.card(user, collections),
                            playerCardAssembler.restoredByEra(user, collections),
                            pick.isRequested());
                })
                .toList();
        int remaining = (int) players.stream().filter(player -> !player.requested()).count();
        return new RecommendationsResponse(players, remaining, gameClock.nextResetAt());
    }

    /**
     * 빈 자리 {@code needed} 개를 채울 사람을 뽑는다. 오늘 이미 뽑혔던 사람은 다시 뽑지 않는다.
     *
     */
    private List<User> pickFor(Long myId, LocalDate today, int needed, Set<Long> pickedToday) {
        LocalDateTime now = gameClock.now();
        Set<Long> excluded = alwaysExcludedFor(myId, now);
        excluded.addAll(pickedToday);
        List<Long> recentlyRecommendedOldestFirst = recommendationRepository.findRecommendedUserIdsSinceOldestFirst(
                myId, today.minusDays(properties.recommendationCooldownDays()));
        Set<Long> recentlyRecommended = new HashSet<>(recentlyRecommendedOldestFirst);
        Predicate<User> notRecentlyRecommended = user -> !recentlyRecommended.contains(user.getId());

        List<User> open = new ArrayList<>();
        Map<Long, Long> friendCounts = new HashMap<>();
        for (int page = 0; ; page++) {
            List<User> scanned = userRepository.findRecentlyActiveFirst(
                    myId, PageRequest.of(page, CANDIDATE_SCAN_LIMIT));
            open.addAll(openCandidates(scanned, excluded, now, friendCounts));
            if (scanned.size() < CANDIDATE_SCAN_LIMIT
                    || open.stream().filter(notRecentlyRecommended).count() >= needed) {
                break;
            }
        }

        LocalDateTime activeSince = now.minus(properties.recommendationActiveWindow());
        List<User> picked = new ArrayList<>();
        pickInto(picked, needed, open, notRecentlyRecommended.and(user -> isActiveSince(user, activeSince)),
                friendCounts);
        pickInto(picked, needed, open, notRecentlyRecommended, friendCounts);
        pickLongestAgoRecommended(picked, needed, open, recentlyRecommendedOldestFirst);
        return picked;
    }

    /**
     * 빈 자리가 남으면 최근에 추천했던 사람으로 채운다. <b>추천한 지 가장 오래된 사람부터</b> 넣으며,
     * 같은 사람을 여러 번 추천했으면 마지막으로 추천한 때를 기준으로 한다. 
     * 
     */
    private void pickLongestAgoRecommended(List<User> picked, int needed, List<User> open,
                                           List<Long> recentlyRecommendedOldestFirst) {
        int remaining = needed - picked.size();
        if (remaining <= 0) {
            return;
        }
        Set<Long> pickedIds = picked.stream().map(User::getId).collect(Collectors.toSet());
        Map<Long, User> openById = open.stream()
                .collect(Collectors.toMap(User::getId, Function.identity(), (first, second) -> first));
        recentlyRecommendedOldestFirst.stream()
                .filter(openById::containsKey)
                .filter(userId -> !pickedIds.contains(userId))
                .limit(remaining)
                .map(openById::get)
                .forEach(picked::add);
    }

    /**
     * 훑은 사용자 중 추천할 수 있는 사람 
     * 친구 수가 적은 순으로 추리므로 센 친구 수를 {@code friendCounts} 에 저장한다.
     */
    private List<User> openCandidates(List<User> scanned, Set<Long> excluded, LocalDateTime now,
                                      Map<Long, Long> friendCounts) {
        List<User> reachable = scanned.stream()
                .filter(user -> !excluded.contains(user.getId()))
                .toList();
        if (reachable.isEmpty()) {
            return List.of();
        }

        List<Long> reachableIds = reachable.stream().map(User::getId).toList();
        friendCounts.putAll(friendCounts(reachableIds));
        Map<Long, Long> inboxCounts = friendRequestRepository
                .countAwaitingByReceivers(reachableIds, FriendRequestStatus.PENDING, now).stream()
                .collect(Collectors.toMap(UserCount::userId, UserCount::count));
        return reachable.stream()
                .filter(user -> friendCounts.getOrDefault(user.getId(), 0L) < properties.friendLimit())
                .filter(user -> inboxCounts.getOrDefault(user.getId(), 0L) < properties.receivedRequestLimit())
                .toList();
    }

    
    private void pickInto(List<User> picked, int needed, List<User> open, Predicate<User> condition,
                          Map<Long, Long> friendCounts) {
        int remaining = needed - picked.size();
        if (remaining <= 0) {
            return;
        }
        Set<Long> pickedIds = picked.stream().map(User::getId).collect(Collectors.toSet());
        List<User> pool = new ArrayList<>(open.stream()
                .filter(user -> !pickedIds.contains(user.getId()))
                .filter(condition)
                .sorted(Comparator.comparingLong((User user) -> friendCounts.getOrDefault(user.getId(), 0L)))
                .limit(properties.recommendationPoolSize())
                .toList());
        Collections.shuffle(pool, ThreadLocalRandom.current());
        picked.addAll(pool.subList(0, Math.min(remaining, pool.size())));
    }

    private static boolean isActiveSince(User user, LocalDateTime since) {
        return user.getLastActiveAt() != null && !user.getLastActiveAt().isBefore(since);
    }

    // 항상 제외하는 사람.
    private Set<Long> alwaysExcludedFor(Long myId, LocalDateTime now) {
        Set<Long> excluded = new HashSet<>();
        friendshipRepository.findAllOf(myId).forEach(friendship -> excluded.add(friendship.otherThan(myId)));
        friendRequestRepository.findAllOutstandingInvolving(myId, now).forEach(request ->
                excluded.add(request.getFromUserId().equals(myId) ? request.getToUserId() : request.getFromUserId()));
        excluded.addAll(blockRepository.findCounterpartIds(myId));
        return excluded;
    }

    private Map<Long, Long> friendCounts(Collection<Long> userIds) {
        Set<Long> targets = new HashSet<>(userIds);
        Map<Long, Long> counts = new HashMap<>();
        for (Friendship friendship : friendshipRepository.findAllTouching(userIds)) {
            if (targets.contains(friendship.getUserIdLow())) {
                counts.merge(friendship.getUserIdLow(), 1L, Long::sum);
            }
            if (targets.contains(friendship.getUserIdHigh())) {
                counts.merge(friendship.getUserIdHigh(), 1L, Long::sum);
            }
        }
        return counts;
    }
}
