package com.example.jamkkaebi.user.repository;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.user.domain.User;
import jakarta.persistence.LockModeType;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.Collection;
import java.util.List;
import java.util.Optional;

public interface UserRepository extends JpaRepository<User, Long> {

    /** 계정 매칭 기준(provider + providerUserId)으로 사용자를 찾는다. */
    Optional<User> findByProviderAndProviderUserId(AuthProvider provider, String providerUserId);

    /** JWT 의 sub 로 들어 있는 친구 코드로 사용자를 찾는다. */
    Optional<User> findByFriendCode(String friendCode);

    /**
     * 친구 코드로 찾으면서 그 행을 <b>바로 잠근다.</b>
     *
     * <p>먼저 평범하게 읽고 나중에 잠그면 소용이 없다 — MySQL 의 기본 격리 수준(REPEATABLE READ)에서는
     * 트랜잭션의 첫 읽기가 스냅샷을 만들고 이후 평범한 읽기가 그 스냅샷을 그대로 쓰기 때문에, 잠금을
     * 얻은 뒤에도 옛 값을 보게 된다. 그러면 동시에 들어온 상자 열기 두 건이 둘 다 "무료 아직 안 씀"과
     * "잔액 충분"을 보고 통과한다. 잠그는 조회를 <b>첫 읽기</b>로 두면 그 시점의 최신 행을 읽는다.
     */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("SELECT u FROM User u WHERE u.friendCode = :friendCode")
    Optional<User> findByFriendCodeForUpdate(@Param("friendCode") String friendCode);

    boolean existsByFriendCode(String friendCode);

    /**
     * 사용자 행을 <b>id 오름차순으로</b> 잠근다.
     *
     * <p>친구 30명·받은 요청 20건 같은 상한은 "세어 보고 넣기"라, 잠그지 않으면 동시 요청 두 개가 둘 다
     * 29를 보고 31명이 된다. 관련된 두 사용자를 먼저 잠그면 그 사이의 요청이 줄을 선다. 순서를 id 로
     * 고정하는 이유는 A→B, B→A 요청이 동시에 들어와도 서로 반대 순서로 잠가 교착되지 않게 하기 위해서다.
     */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("SELECT u FROM User u WHERE u.id IN :ids ORDER BY u.id")
    List<User> lockAllByIds(@Param("ids") Collection<Long> ids);

    /**
     * 정성을 적립한다. <b>읽고 더해서 쓰지 않고</b> 한 문장으로 더한다.
     *
     * <p>엔티티 값을 읽어 더하면, 같은 사용자의 다른 트랜잭션(방문 보상과 선물 환산이 동시에 오는 경우)이
     * 읽은 옛 잔액으로 덮어써 적립분이 사라진다.
     */
    @Modifying(flushAutomatically = true)
    @Query("UPDATE User u SET u.jeongseong = u.jeongseong + :amount WHERE u.id = :userId")
    int addJeongseong(@Param("userId") Long userId, @Param("amount") int amount);

    /**
     * 나를 뺀 사용자를 최근 접속순으로 가져온다. 접속 기록이 없는 사용자는 맨 뒤다.
     *
     * <p>친구 추천 후보를 만든다. 최근 접속 여부로 거르지 않는 이유는, 최근 접속자만으로 3명을 채우지 못할
     * 때 오래 접속하지 않은 사용자로 나머지를 채워야 하기 때문이다.
     */
    @Query("""
            SELECT u FROM User u
             WHERE u.id <> :excludedId
             ORDER BY u.lastActiveAt DESC NULLS LAST, u.id DESC
            """)
    List<User> findRecentlyActiveFirst(@Param("excludedId") Long excludedId, Pageable pageable);
}
