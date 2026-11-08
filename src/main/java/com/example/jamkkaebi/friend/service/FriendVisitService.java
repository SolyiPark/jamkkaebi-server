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

/**
 * 친구 전시관 방문 — 열람과 방문 보상.
 *
 */
@Service
public class FriendVisitService {

    private static final int RECENT_VISIT_DAYS = 30; //방문 기록은 최대 30일

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

    /**
     * 친구 전시관 스냅샷. 열면 오늘 이 친구를 방문한 것으로 남긴다 (같은 날은 한 번). 보상은 주지 않는다.
     *
     * 아무것도 놓이지 않은 전시관은 오류가 아니라 <b>기본 빈 전시관</b>이다. (그리드만 싣는다)
     */
    @Transactional
    public FriendExhibitionResponse exhibition(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        User friend = requireFriend(me, rawFriendCode);
        friendUsers.lock(me.getId());
        LocalDate today = gameClock.today();
        recordVisit(me.getId(), friend.getId(), today);
        ExhibitionSnapshotService.Snapshot snapshot = snapshotService.findOrEmpty(friend.getId());

        return new FriendExhibitionResponse(
                playerCardAssembler.card(friend),
                snapshot.view().grid(),
                snapshot.view().spirits(),
                snapshot.view().buildings(),
                snapshot.capturedAt(),
                giftService.giftStates(me.getId(), List.of(friend.getId()), today).get(friend.getId()),
                !visitLogRepository.existsByVisitorIdAndVisitDateAndRewardedTrue(me.getId(), today),
                (int) visitLogRepository.countByVisitorIdAndHostIdAndVisitDateGreaterThanEqual(
                        me.getId(), friend.getId(), today.minusDays(RECENT_VISIT_DAYS - 1)));
    }

    /**
     * 오늘 아직 방문 보상을 받지 않았으면 정성을 준다. 보상은 몇 명을 방문하든 하루 1회다.
     *
     * <p>먼저 오늘의 방문 행을 확보하고(열람 때 이미 남았으면 그 행을 쓴다) 그 뒤에 보상을 판단한다.
     * 열람과 보상 요청이 동시에 와도 사용자 행 잠금으로 하나씩 처리되고, 어느 쪽이 먼저 와도 결과가 같다.
     */
    @Transactional
    public VisitRewardResponse claimReward(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        User friend = requireFriend(me, rawFriendCode);
        friendUsers.lock(me.getId());
        LocalDate today = gameClock.today();

        VisitLog visit = recordVisit(me.getId(), friend.getId(), today);
        if (visitLogRepository.existsByVisitorIdAndVisitDateAndRewardedTrue(me.getId(), today)) {
            return new VisitRewardResponse(0, true);
        }

        int reward = properties.visitRewardJeongseong();
        visit.markRewarded(reward);
        userRepository.addJeongseong(me.getId(), reward);
        return new VisitRewardResponse(reward, true);
    }

    /**
     * 오늘의 방문 행을 찾고, 없으면 보상 없이 만든다.
     *
     * <p><b>사용자 행을 잠근 뒤에 부른다.</b> 같은 사용자의 열람과 보상 요청이 겹쳐도 행이 두 번 만들어지지 않는다.
     */
    private VisitLog recordVisit(Long visitorId, Long hostId, LocalDate today) {
        return visitLogRepository.findByVisitorIdAndHostIdAndVisitDate(visitorId, hostId, today)
                .orElseGet(() -> visitLogRepository.save(VisitLog.withoutReward(visitorId, hostId, today)));
    }

    // 친구가 아니면 없는 코드. 차단 관계와 구분하지 않는다
    private User requireFriend(User me, String rawFriendCode) {
        return friendUsers.findByCode(rawFriendCode)
                .filter(other -> !other.getId().equals(me.getId()))
                .filter(other -> socialGraph.areFriends(me.getId(), other.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_NOT_FOUND));
    }
}
