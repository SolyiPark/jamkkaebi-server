package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.domain.VisitLog;
import com.example.jamkkaebi.friend.dto.response.FriendExhibitionResponse;
import com.example.jamkkaebi.friend.dto.response.VisitRewardResponse;
import com.example.jamkkaebi.friend.repository.VisitLogRepository;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.util.List;
import java.util.Optional;

/**
 * 친구 전시관 방문 — 열람과 방문 보상.
 *
 */
@Service
public class FriendVisitService {

    private final FriendUsers friendUsers;
    private final SocialGraph socialGraph;
    private final VisitLogRepository visitLogRepository;
    private final UserRepository userRepository;
    private final GiftService giftService;
    private final ExhibitionSnapshotService snapshotService;
    private final PlayerCardAssembler playerCardAssembler;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public FriendVisitService(FriendUsers friendUsers,
                              SocialGraph socialGraph,
                              VisitLogRepository visitLogRepository,
                              UserRepository userRepository,
                              GiftService giftService,
                              ExhibitionSnapshotService snapshotService,
                              PlayerCardAssembler playerCardAssembler,
                              FriendProperties properties,
                              GameClock gameClock) {
        this.friendUsers = friendUsers;
        this.socialGraph = socialGraph;
        this.visitLogRepository = visitLogRepository;
        this.userRepository = userRepository;
        this.giftService = giftService;
        this.snapshotService = snapshotService;
        this.playerCardAssembler = playerCardAssembler;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    // 친구 전시관 스냅샷
    @Transactional(readOnly = true)
    public FriendExhibitionResponse exhibition(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        User friend = requireFriend(me, rawFriendCode);
        LocalDate today = gameClock.today();
        Optional<ExhibitionSnapshotService.Snapshot> snapshot = snapshotService.find(friend.getId());

        return new FriendExhibitionResponse(
                playerCardAssembler.card(friend),
                snapshot.map(found -> found.view().grid()).orElse(null),
                snapshot.map(found -> found.view().spirits()).orElse(List.of()),
                snapshot.map(found -> found.view().buildings()).orElse(List.of()),
                snapshot.map(ExhibitionSnapshotService.Snapshot::capturedAt).orElse(null),
                giftService.giftStates(me.getId(), List.of(friend.getId()), today).get(friend.getId()),
                !visitLogRepository.existsByVisitorIdAndVisitDateAndRewardedTrue(me.getId(), today));
    }

    // 방문을 기록하고, 오늘 첫 보상이면 정성을 준다
    @Transactional
    public VisitRewardResponse claimReward(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        User friend = requireFriend(me, rawFriendCode);
        friendUsers.lock(me.getId());
        LocalDate today = gameClock.today();

        if (visitLogRepository.findByVisitorIdAndHostIdAndVisitDate(me.getId(), friend.getId(), today).isPresent()) {
            return new VisitRewardResponse(0, true);
        }
        if (visitLogRepository.existsByVisitorIdAndVisitDateAndRewardedTrue(me.getId(), today)) {
            visitLogRepository.save(VisitLog.withoutReward(me.getId(), friend.getId(), today));
            return new VisitRewardResponse(0, true);
        }

        int reward = properties.visitRewardJeongseong();
        visitLogRepository.save(VisitLog.rewarded(me.getId(), friend.getId(), today, reward));
        userRepository.addJeongseong(me.getId(), reward);
        return new VisitRewardResponse(reward, true);
    }

    // 친구가 아니면 없는 코드·차단 관계와 구분하지 않는다
    private User requireFriend(User me, String rawFriendCode) {
        return friendUsers.findByCode(rawFriendCode)
                .filter(other -> !other.getId().equals(me.getId()))
                .filter(other -> socialGraph.areFriends(me.getId(), other.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_NOT_FOUND));
    }
}
